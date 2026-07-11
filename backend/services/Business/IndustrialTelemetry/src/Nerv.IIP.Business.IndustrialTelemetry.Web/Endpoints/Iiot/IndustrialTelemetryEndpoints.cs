using System.Diagnostics.CodeAnalysis;
using FastEndpoints;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlChannelBindingAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Auth;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Ops;
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

public sealed record CreateTelemetryTagRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string TagKey,
    string ValueType,
    string UnitCode,
    string SamplingPolicy,
    bool IsWritable = false,
    decimal? ControlMinValue = null,
    decimal? ControlMaxValue = null,
    IReadOnlyCollection<string>? ControlAllowedValues = null);
public sealed record CreateTelemetryTagResponse(TelemetryTagId TelemetryTagId);
public sealed record ListTelemetryTagsRequest(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId, int Skip = 0, int Take = 100);
public sealed record GetTelemetryTagCurrentValueRequest(string OrganizationId, string EnvironmentId, string DeviceAssetId, string TagKey);
public sealed record CreateDeviceControlCommandRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string CommandType,
    string? TagKey,
    string? Value,
    IReadOnlyDictionary<string, string>? Parameters,
    string RequestedBy,
    string Reason,
    string IdempotencyKey,
    string CorrelationId);
public sealed record CreateDeviceControlCommandResponse(string OperationTaskId, string Status, OperationApprovalSummary? Approval);
public sealed record CreateOrUpdateAlarmRuleRequest(
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
    decimal DeadbandValue = 0m,
    int OnDelaySeconds = 0,
    int OffDelaySeconds = 0,
    int MinDurationSeconds = 0,
    string? Priority = null);
public sealed record CreateOrUpdateAlarmRuleResponse(AlarmRuleId AlarmRuleId);
public sealed record ListAlarmRulesRequest(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId, bool? IsEnabled, int Skip = 0, int Take = 100);
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
    decimal? FirstValue,
    decimal? LastValue,
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
    string? ClearReason,
    string? Priority = null,
    string? TagKey = null,
    decimal? ObservedValue = null,
    decimal? ThresholdValue = null,
    string? UnitCode = null);
public sealed record PostAlarmEventResponse(AlarmEventId AlarmEventId);
public sealed record AcknowledgeAlarmRequest(string OrganizationId, string EnvironmentId, DateTimeOffset AcknowledgedAtUtc, string AcknowledgedBy);
public sealed record ShelveAlarmRequest(string OrganizationId, string EnvironmentId, DateTimeOffset ShelvedAtUtc, int DurationMinutes, string ShelvedBy, string? Reason);
public sealed record UnshelveAlarmRequest(string OrganizationId, string EnvironmentId, DateTimeOffset? UnshelvedAtUtc);
public sealed record RunAlarmEscalationsRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset AsOfUtc,
    int UnacknowledgedTimeoutMinutes,
    IReadOnlyCollection<string> SeverityLevels,
    IReadOnlyCollection<string> RecipientRefs,
    int MaxAlarms = 500);
public sealed record AlarmLifecycleResponse(AlarmEventId AlarmEventId);
public sealed record RunAlarmEscalationsResponse(int EscalatedCount, IReadOnlyCollection<AlarmEventId> AlarmEventIds);
public sealed record ListAlarmEventsRequest(
    string? OrganizationId,
    string? EnvironmentId,
    string? DeviceAssetId,
    string? Status,
    int Skip = 0,
    int Take = 100,
    string? DeviceAssetIds = null);
public sealed record QueryDeviceTimelineRequest(string DeviceAssetId, string? OrganizationId, string? EnvironmentId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc);
public sealed record QueryOeeRequest(string OrganizationId, string EnvironmentId, string DeviceAssetId, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc);
public sealed record QueryRuntimeHoursRequest(string OrganizationId, string EnvironmentId, string DeviceAssetId, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc);
public sealed record GetDeviceRuntimeAvailabilityRequest(string DeviceAssetId, string OrganizationId, string EnvironmentId, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc, int FreshnessMaxAgeMinutes = 60);
public sealed record QueryRuntimeAvailabilityRequest(string OrganizationId, string EnvironmentId, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc, string? DeviceAssetIds, string? WorkCenterIds, int FreshnessMaxAgeMinutes = 60);
public sealed record GetDeviceCurrentStateRequest(string DeviceAssetId, string OrganizationId, string EnvironmentId, DateTimeOffset? AsOfUtc, int FreshnessMaxAgeMinutes = 60);
public sealed record GetDeviceControlCommandRequest(string OrganizationId, string EnvironmentId, string DeviceAssetId);
public sealed record ListDeviceControlCommandsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceAssetId,
    string? Status,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Skip = 0,
    int Take = 100);
public sealed record CreateOrUpdateDeviceControlBindingRequest(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string ConnectorHostId,
    string InstanceKey);
public sealed record CreateOrUpdateDeviceControlBindingResponse(DeviceControlChannelBindingId DeviceControlChannelBindingId);
public sealed record DisableDeviceControlBindingRequest(string OrganizationId, string EnvironmentId, string? Reason);
public sealed record DisableDeviceControlBindingResponse(DeviceControlChannelBindingId DeviceControlChannelBindingId);
public sealed record ListDeviceControlBindingsRequest(string? OrganizationId, string? EnvironmentId, string? DeviceAssetId, bool? IsActive, int Skip = 0, int Take = 100);

public sealed class CreateTelemetryTagEndpoint(ISender sender) : IndustrialTelemetryEndpoint<CreateTelemetryTagRequest, ResponseData<CreateTelemetryTagResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<CreateTelemetryTagEndpoint>());

    public override async Task HandleAsync(CreateTelemetryTagRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateTelemetryTagCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.TagKey, req.ValueType, req.UnitCode, req.SamplingPolicy, req.IsWritable, req.ControlMinValue, req.ControlMaxValue, req.ControlAllowedValues), ct);
        await Send.OkAsync(new CreateTelemetryTagResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateDeviceControlCommandEndpoint(ISender sender) : IndustrialTelemetryEndpoint<CreateDeviceControlCommandRequest, ResponseData<CreateDeviceControlCommandResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<CreateDeviceControlCommandEndpoint>());

    public override async Task HandleAsync(CreateDeviceControlCommandRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new CreateDeviceControlCommandCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.DeviceAssetId,
            req.CommandType,
            req.TagKey,
            req.Value,
            req.Parameters,
            req.RequestedBy,
            req.Reason,
            req.IdempotencyKey,
            req.CorrelationId), ct);
        await Send.OkAsync(new CreateDeviceControlCommandResponse(result.OperationTaskId, result.Status, result.Approval).AsResponseData(), cancellation: ct);
    }
}

public sealed class GetDeviceControlCommandEndpoint(ISender sender) : IndustrialTelemetryEndpoint<GetDeviceControlCommandRequest, ResponseData<DeviceControlCommandResult>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<GetDeviceControlCommandEndpoint>());

    public override async Task HandleAsync(GetDeviceControlCommandRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new GetDeviceControlCommandQuery(Route<string>("commandId")!, req.OrganizationId, req.EnvironmentId, req.DeviceAssetId), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListDeviceControlCommandsEndpoint(ISender sender) : IndustrialTelemetryEndpoint<ListDeviceControlCommandsRequest, ResponseData<PagedListResponse<DeviceControlCommandListItem>>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<ListDeviceControlCommandsEndpoint>());

    public override async Task HandleAsync(ListDeviceControlCommandsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListDeviceControlCommandsQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.Status, req.FromUtc, req.ToUtc, req.Skip, req.Take), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateOrUpdateDeviceControlBindingEndpoint(ISender sender) : IndustrialTelemetryEndpoint<CreateOrUpdateDeviceControlBindingRequest, ResponseData<CreateOrUpdateDeviceControlBindingResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<CreateOrUpdateDeviceControlBindingEndpoint>());

    public override async Task HandleAsync(CreateOrUpdateDeviceControlBindingRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateOrUpdateDeviceControlBindingCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.ConnectorHostId, req.InstanceKey), ct);
        await Send.OkAsync(new CreateOrUpdateDeviceControlBindingResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class DisableDeviceControlBindingEndpoint(ISender sender) : IndustrialTelemetryEndpoint<DisableDeviceControlBindingRequest, ResponseData<DisableDeviceControlBindingResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<DisableDeviceControlBindingEndpoint>());

    public override async Task HandleAsync(DisableDeviceControlBindingRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new DisableDeviceControlBindingCommand(req.OrganizationId, req.EnvironmentId, Route<string>("deviceAssetId")!, req.Reason), ct);
        await Send.OkAsync(new DisableDeviceControlBindingResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListDeviceControlBindingsEndpoint(ISender sender) : IndustrialTelemetryEndpoint<ListDeviceControlBindingsRequest, ResponseData<PagedListResponse<DeviceControlBindingListItem>>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<ListDeviceControlBindingsEndpoint>());

    public override async Task HandleAsync(ListDeviceControlBindingsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListDeviceControlBindingsQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.IsActive, req.Skip, req.Take), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListTelemetryTagsEndpoint(ISender sender) : IndustrialTelemetryEndpoint<ListTelemetryTagsRequest, ResponseData<PagedListResponse<TelemetryTagListItem>>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<ListTelemetryTagsEndpoint>());

    public override async Task HandleAsync(ListTelemetryTagsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListTelemetryTagsQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.Skip, req.Take), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetTelemetryTagCurrentValueEndpoint(ISender sender) : IndustrialTelemetryEndpoint<GetTelemetryTagCurrentValueRequest, ResponseData<TelemetryTagCurrentValueResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<GetTelemetryTagCurrentValueEndpoint>());

    public override async Task HandleAsync(GetTelemetryTagCurrentValueRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new GetTelemetryTagCurrentValueQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.TagKey), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class CreateOrUpdateAlarmRuleEndpoint(ISender sender) : IndustrialTelemetryEndpoint<CreateOrUpdateAlarmRuleRequest, ResponseData<CreateOrUpdateAlarmRuleResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<CreateOrUpdateAlarmRuleEndpoint>());

    public override async Task HandleAsync(CreateOrUpdateAlarmRuleRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new CreateOrUpdateAlarmRuleCommand(
            req.OrganizationId,
            req.EnvironmentId,
            req.DeviceAssetId,
            req.RuleCode,
            req.AlarmCode,
            req.Severity,
            req.TagKey,
            req.ComparisonOperator,
            req.ThresholdValue,
            req.UnitCode,
            req.IsEnabled,
            req.DeadbandValue,
            req.OnDelaySeconds,
            req.OffDelaySeconds,
            req.MinDurationSeconds,
            req.Priority), ct);
        await Send.OkAsync(new CreateOrUpdateAlarmRuleResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListAlarmRulesEndpoint(ISender sender) : IndustrialTelemetryEndpoint<ListAlarmRulesRequest, ResponseData<PagedListResponse<AlarmRuleListItem>>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<ListAlarmRulesEndpoint>());

    public override async Task HandleAsync(ListAlarmRulesRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListAlarmRulesQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.IsEnabled, req.Skip, req.Take), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class RecordTelemetrySampleEndpoint(ISender sender) : IndustrialTelemetryEndpoint<RecordTelemetrySampleRequest, ResponseData<RecordTelemetrySampleResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<RecordTelemetrySampleEndpoint>());

    public override async Task HandleAsync(RecordTelemetrySampleRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RecordTelemetrySampleCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.TagKey, req.BucketStartUtc, req.BucketEndUtc, req.SampleCount, req.MinValue, req.MaxValue, req.AverageValue, req.SourceSequence, req.SourceSystem, req.SourceConnector, req.FirstValue, req.LastValue, req.DeviceState, req.StateOccurredAtUtc), ct);
        await Send.OkAsync(new RecordTelemetrySampleResponse(result.TelemetrySummaryId, result.DeviceStateSnapshotId).AsResponseData(), cancellation: ct);
    }
}

public sealed class PostAlarmEventEndpoint(ISender sender) : IndustrialTelemetryEndpoint<PostAlarmEventRequest, ResponseData<PostAlarmEventResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<PostAlarmEventEndpoint>());

    public override async Task HandleAsync(PostAlarmEventRequest req, CancellationToken ct)
    {
        var id = req.ClearedAtUtc is null
            ? await sender.Send(new RaiseAlarmCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.AlarmCode, req.Severity, req.RaisedAtUtc, req.ExternalAlarmId, req.Priority, req.TagKey, req.ObservedValue, req.ThresholdValue, req.UnitCode), ct)
            : await sender.Send(new ClearAlarmCommand(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.AlarmCode, req.ExternalAlarmId, req.ClearedAtUtc.Value, req.ClearedBy ?? string.Empty, req.ClearReason), ct);
        await Send.OkAsync(new PostAlarmEventResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class AcknowledgeAlarmEndpoint(ISender sender) : IndustrialTelemetryEndpoint<AcknowledgeAlarmRequest, ResponseData<AlarmLifecycleResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<AcknowledgeAlarmEndpoint>());

    public override async Task HandleAsync(AcknowledgeAlarmRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new AcknowledgeAlarmCommand(AlarmEventRouteIds.Parse(Route<string>("alarmEventId")), req.OrganizationId, req.EnvironmentId, req.AcknowledgedAtUtc, req.AcknowledgedBy), ct);
        await Send.OkAsync(new AlarmLifecycleResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ShelveAlarmEndpoint(ISender sender) : IndustrialTelemetryEndpoint<ShelveAlarmRequest, ResponseData<AlarmLifecycleResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<ShelveAlarmEndpoint>());

    public override async Task HandleAsync(ShelveAlarmRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new ShelveAlarmCommand(AlarmEventRouteIds.Parse(Route<string>("alarmEventId")), req.OrganizationId, req.EnvironmentId, req.ShelvedAtUtc, req.DurationMinutes, req.ShelvedBy, req.Reason), ct);
        await Send.OkAsync(new AlarmLifecycleResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class UnshelveAlarmEndpoint(ISender sender) : IndustrialTelemetryEndpoint<UnshelveAlarmRequest, ResponseData<AlarmLifecycleResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<UnshelveAlarmEndpoint>());

    public override async Task HandleAsync(UnshelveAlarmRequest req, CancellationToken ct)
    {
        var id = await sender.Send(new UnshelveAlarmCommand(AlarmEventRouteIds.Parse(Route<string>("alarmEventId")), req.OrganizationId, req.EnvironmentId, req.UnshelvedAtUtc ?? DateTimeOffset.UtcNow), ct);
        await Send.OkAsync(new AlarmLifecycleResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class RunAlarmEscalationsEndpoint(ISender sender) : IndustrialTelemetryEndpoint<RunAlarmEscalationsRequest, ResponseData<RunAlarmEscalationsResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<RunAlarmEscalationsEndpoint>());

    public override async Task HandleAsync(RunAlarmEscalationsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new RunAlarmEscalationsCommand(req.OrganizationId, req.EnvironmentId, req.AsOfUtc, req.UnacknowledgedTimeoutMinutes, req.SeverityLevels, req.RecipientRefs, req.MaxAlarms), ct);
        await Send.OkAsync(new RunAlarmEscalationsResponse(result.EscalatedCount, result.AlarmEventIds).AsResponseData(), cancellation: ct);
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

public sealed class ListAlarmEventsEndpoint(ISender sender) : IndustrialTelemetryEndpoint<ListAlarmEventsRequest, ResponseData<PagedListResponse<AlarmEventListItem>>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<ListAlarmEventsEndpoint>());

    public override async Task HandleAsync(ListAlarmEventsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new ListAlarmEventsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.DeviceAssetId,
            req.Status,
            req.Skip,
            req.Take,
            req.DeviceAssetIds), ct);
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

public sealed class QueryOeeEndpoint(ISender sender) : IndustrialTelemetryEndpoint<QueryOeeRequest, ResponseData<OeeResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<QueryOeeEndpoint>());

    public override async Task HandleAsync(QueryOeeRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new QueryOeeQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.WindowStartUtc, req.WindowEndUtc), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class QueryRuntimeHoursEndpoint(ISender sender) : IndustrialTelemetryEndpoint<QueryRuntimeHoursRequest, ResponseData<RuntimeHoursResponse>>
{
    public override void Configure() => ConfigureIndustrialTelemetryContract(IndustrialTelemetryEndpointContracts.Get<QueryRuntimeHoursEndpoint>());

    public override async Task HandleAsync(QueryRuntimeHoursRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new QueryRuntimeHoursQuery(req.OrganizationId, req.EnvironmentId, req.DeviceAssetId, req.WindowStartUtc, req.WindowEndUtc), ct);
        await Send.OkAsync(result.AsResponseData(), cancellation: ct);
    }
}

public sealed class ListTelemetryTagsRequestValidator : Validator<ListTelemetryTagsRequest>
{
    public ListTelemetryTagsRequestValidator()
    {
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class GetTelemetryTagCurrentValueRequestValidator : Validator<GetTelemetryTagCurrentValueRequest>
{
    public GetTelemetryTagCurrentValueRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TagKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class CreateDeviceControlCommandRequestValidator : Validator<CreateDeviceControlCommandRequest>
{
    public CreateDeviceControlCommandRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CommandType)
            .NotEmpty()
            .MaximumLength(50)
            .Must(DeviceControlCommandValidation.IsSupportedCommandType)
            .WithMessage("Device control command type must be write-tag, start-stop or parameter-set.");
        When(x => DeviceControlCommandValidation.IsSingleTagCommand(x.CommandType), () =>
        {
            RuleFor(x => x.TagKey).NotEmpty().MaximumLength(150);
            RuleFor(x => x.Value).NotEmpty().MaximumLength(256);
        });
        When(x => DeviceControlCommandValidation.IsParameterSetCommand(x.CommandType), () =>
        {
            RuleFor(x => x.Parameters).NotEmpty();
            RuleForEach(x => x.Parameters!.Keys).NotEmpty().MaximumLength(150);
            RuleForEach(x => x.Parameters!.Values).NotEmpty().MaximumLength(256);
        });
        RuleFor(x => x.RequestedBy).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(150);
    }
}

public sealed class ListAlarmRulesRequestValidator : Validator<ListAlarmRulesRequest>
{
    public ListAlarmRulesRequestValidator()
    {
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListAlarmEventsRequestValidator : Validator<ListAlarmEventsRequest>
{
    public ListAlarmEventsRequestValidator()
    {
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class GetDeviceControlCommandRequestValidator : Validator<GetDeviceControlCommandRequest>
{
    public GetDeviceControlCommandRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
    }
}

public sealed class ListDeviceControlCommandsRequestValidator : Validator<ListDeviceControlCommandsRequest>
{
    public ListDeviceControlCommandsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).MaximumLength(150);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
        RuleFor(x => x.ToUtc).GreaterThan(x => x.FromUtc).When(x => x.FromUtc is not null && x.ToUtc is not null);
    }
}

public sealed class CreateOrUpdateDeviceControlBindingRequestValidator : Validator<CreateOrUpdateDeviceControlBindingRequest>
{
    public CreateOrUpdateDeviceControlBindingRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ConnectorHostId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.InstanceKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class DisableDeviceControlBindingRequestValidator : Validator<DisableDeviceControlBindingRequest>
{
    public DisableDeviceControlBindingRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}

public sealed class ListDeviceControlBindingsRequestValidator : Validator<ListDeviceControlBindingsRequest>
{
    public ListDeviceControlBindingsRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed record IndustrialTelemetryEndpointContract(Type EndpointType, string HttpMethod, string Route, string PermissionCode, string AuthorizationPolicy, string OperationId);

public static class IndustrialTelemetryEndpointContracts
{
    public static readonly IReadOnlyCollection<IndustrialTelemetryEndpointContract> All =
    [
        new(typeof(CreateTelemetryTagEndpoint), "POST", "/api/business/v1/iiot/tags", IndustrialTelemetryPermissionCodes.TagsManage, InternalServiceAuthorizationPolicy.Name, "createBusinessIiotTelemetryTag"),
        new(typeof(ListTelemetryTagsEndpoint), "GET", "/api/business/v1/iiot/tags", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "listBusinessIiotTelemetryTags"),
        new(typeof(GetTelemetryTagCurrentValueEndpoint), "GET", "/api/business/v1/iiot/tags/current-value", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "getBusinessIiotTelemetryTagCurrentValue"),
        new(typeof(CreateDeviceControlCommandEndpoint), "POST", "/api/business/v1/iiot/device-control-commands", IndustrialTelemetryPermissionCodes.DeviceControlWrite, InternalServiceAuthorizationPolicy.Name, "createBusinessIiotDeviceControlCommand"),
        new(typeof(GetDeviceControlCommandEndpoint), "GET", "/api/business/v1/iiot/device-control-commands/{commandId}", IndustrialTelemetryPermissionCodes.DeviceControlRead, InternalServiceAuthorizationPolicy.Name, "getBusinessIiotDeviceControlCommand"),
        new(typeof(ListDeviceControlCommandsEndpoint), "GET", "/api/business/v1/iiot/device-control-commands", IndustrialTelemetryPermissionCodes.DeviceControlRead, InternalServiceAuthorizationPolicy.Name, "listBusinessIiotDeviceControlCommands"),
        new(typeof(CreateOrUpdateDeviceControlBindingEndpoint), "POST", "/api/business/v1/iiot/device-control-bindings", IndustrialTelemetryPermissionCodes.DeviceControlManage, InternalServiceAuthorizationPolicy.Name, "createOrUpdateBusinessIiotDeviceControlBinding"),
        new(typeof(DisableDeviceControlBindingEndpoint), "POST", "/api/business/v1/iiot/device-control-bindings/{deviceAssetId}/disable", IndustrialTelemetryPermissionCodes.DeviceControlManage, InternalServiceAuthorizationPolicy.Name, "disableBusinessIiotDeviceControlBinding"),
        new(typeof(ListDeviceControlBindingsEndpoint), "GET", "/api/business/v1/iiot/device-control-bindings", IndustrialTelemetryPermissionCodes.DeviceControlRead, InternalServiceAuthorizationPolicy.Name, "listBusinessIiotDeviceControlBindings"),
        new(typeof(CreateOrUpdateAlarmRuleEndpoint), "POST", "/api/business/v1/iiot/alarm-rules", IndustrialTelemetryPermissionCodes.AlarmRulesManage, InternalServiceAuthorizationPolicy.Name, "createOrUpdateBusinessIiotAlarmRule"),
        new(typeof(ListAlarmRulesEndpoint), "GET", "/api/business/v1/iiot/alarm-rules", IndustrialTelemetryPermissionCodes.AlarmsRead, InternalServiceAuthorizationPolicy.Name, "listBusinessIiotAlarmRules"),
        new(typeof(RecordTelemetrySampleEndpoint), "POST", "/api/business/v1/iiot/samples", IndustrialTelemetryPermissionCodes.TelemetryWrite, InternalServiceAuthorizationPolicy.Name, "recordBusinessIiotTelemetrySample"),
        new(typeof(PostAlarmEventEndpoint), "POST", "/api/business/v1/iiot/alarms", IndustrialTelemetryPermissionCodes.AlarmsWrite, InternalServiceAuthorizationPolicy.Name, "raiseBusinessIiotAlarm"),
        new(typeof(ListAlarmEventsEndpoint), "GET", "/api/business/v1/iiot/alarms", IndustrialTelemetryPermissionCodes.AlarmsRead, InternalServiceAuthorizationPolicy.Name, "listBusinessIiotAlarms"),
        new(typeof(AcknowledgeAlarmEndpoint), "POST", "/api/business/v1/iiot/alarms/{alarmEventId}/acknowledge", IndustrialTelemetryPermissionCodes.AlarmsWrite, InternalServiceAuthorizationPolicy.Name, "acknowledgeBusinessIiotAlarm"),
        new(typeof(ShelveAlarmEndpoint), "POST", "/api/business/v1/iiot/alarms/{alarmEventId}/shelve", IndustrialTelemetryPermissionCodes.AlarmsWrite, InternalServiceAuthorizationPolicy.Name, "shelveBusinessIiotAlarm"),
        new(typeof(UnshelveAlarmEndpoint), "POST", "/api/business/v1/iiot/alarms/{alarmEventId}/unshelve", IndustrialTelemetryPermissionCodes.AlarmsWrite, InternalServiceAuthorizationPolicy.Name, "unshelveBusinessIiotAlarm"),
        new(typeof(RunAlarmEscalationsEndpoint), "POST", "/api/business/v1/iiot/alarms/escalations/run", IndustrialTelemetryPermissionCodes.AlarmsWrite, InternalServiceAuthorizationPolicy.Name, "runBusinessIiotAlarmEscalations"),
        new(typeof(QueryDeviceTimelineEndpoint), "GET", "/api/business/v1/iiot/devices/{deviceAssetId}/timeline", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "queryBusinessIiotDeviceTimeline"),
        new(typeof(QueryOeeEndpoint), "GET", "/api/business/v1/iiot/oee", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "queryBusinessIiotOee"),
        new(typeof(QueryRuntimeHoursEndpoint), "GET", "/api/business/v1/iiot/runtime-hours", IndustrialTelemetryPermissionCodes.TelemetryRead, InternalServiceAuthorizationPolicy.Name, "queryBusinessIiotRuntimeHours"),
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

internal static class AlarmEventRouteIds
{
    public static AlarmEventId Parse(string? value)
    {
        if (!Guid.TryParse(value, out var id))
        {
            throw new KnownException("Alarm event id is invalid.");
        }

        return new AlarmEventId(id);
    }
}
