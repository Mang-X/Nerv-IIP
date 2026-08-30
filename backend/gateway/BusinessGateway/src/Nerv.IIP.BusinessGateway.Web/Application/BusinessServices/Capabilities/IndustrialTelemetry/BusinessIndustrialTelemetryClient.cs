using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.EquipmentRuntime;
using BusinessOeeAggregateBucket = Nerv.IIP.Contracts.IndustrialTelemetry.OeeAggregateBucket;
using BusinessOeeAggregateDimension = Nerv.IIP.Contracts.IndustrialTelemetry.OeeAggregateDimension;
using BusinessOeeAggregateRequest = Nerv.IIP.Contracts.IndustrialTelemetry.QueryOeeAggregateBucketsRequest;
using BusinessOeeAggregateResponse = Nerv.IIP.Contracts.IndustrialTelemetry.OeeAggregateBucketsResponse;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessIndustrialTelemetryClient
{
    Task<BusinessConsoleConnectorTagCoverageResponse> GetConnectorTagCoverageAsync(
        string internalBearerToken,
        BusinessConsoleConnectorTagCoverageRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryTagListResponse> ListTagsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryTagCurrentValueResponse> GetTagCurrentValueAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagCurrentValueRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryAlarmRuleListResponse> ListAlarmRulesAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmRuleListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse> CreateOrUpdateAlarmRuleAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRecordTelemetrySampleResponse> RecordSampleAsync(
        string internalBearerToken,
        BusinessConsoleRecordTelemetrySampleRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsolePostTelemetryAlarmResponse> PostAlarmAsync(
        string internalBearerToken,
        BusinessConsolePostTelemetryAlarmRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryAlarmEventListResponse> ListAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryHistoryResponse> QueryHistoryAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleTelemetryHistoryRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryOeeResponse> QueryOeeAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryOeeRequest request,
        CancellationToken cancellationToken);

    Task<BusinessOeeAggregateResponse> QueryOeeAggregatesAsync(
        string internalBearerToken,
        BusinessOeeAggregateRequest request,
        CancellationToken cancellationToken);

    Task<EquipmentRuntimeAvailabilityResponse> GetRuntimeAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryRuntimeHoursResponse> QueryRuntimeHoursAsync(string internalBearerToken, BusinessConsoleTelemetryRuntimeHoursRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<EquipmentRuntimeAvailabilityResponse> GetDeviceRuntimeAvailabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<EquipmentRuntimeCurrentStateResponse> GetDeviceCurrentStateAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEquipmentHealthResponse> GetEquipmentHealthAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    Task<BusinessConsoleEquipmentAlarmListPageResponse> ListActiveAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAlarmListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAlarmLifecycleResponse> AcknowledgeAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleAcknowledgeAlarmRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAlarmLifecycleResponse> ShelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleShelveAlarmRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAlarmLifecycleResponse> UnshelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleUnshelveAlarmRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryDeviceControlCommandResponse> CreateDeviceControlCommandAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandRequest request,
        string requestedBy,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryDeviceControlCommandDetail> GetDeviceControlCommandAsync(
        string internalBearerToken,
        string commandId,
        BusinessConsoleTelemetryDeviceControlCommandContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryDeviceControlCommandListResponse> ListDeviceControlCommandsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleTelemetryDeviceControlBindingListResponse> ListDeviceControlBindingsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlBindingListRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingResponse> CreateOrUpdateDeviceControlBindingAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleDisableTelemetryDeviceControlBindingResponse> DisableDeviceControlBindingAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleDisableTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpBusinessIndustrialTelemetryClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessIndustrialTelemetryClient
{
    private static readonly HashSet<string> EquipmentHealthRuleCodes = new(StringComparer.Ordinal)
    {
        "threshold-proximity",
        "runtime-hours-24h",
        "alarm-frequency-24h",
        "sustained-exceedance",
        "trend-growth",
    };
    private static readonly JsonSerializerOptions EquipmentHealthJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        },
    };

    public Task<BusinessConsoleConnectorTagCoverageResponse> GetConnectorTagCoverageAsync(
        string internalBearerToken,
        BusinessConsoleConnectorTagCoverageRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleConnectorTagCoverageResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/connectors/{Uri.EscapeDataString(request.ConnectorId)}/tag-coverage?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId)),
            null,
            cancellationToken);

    public async Task<BusinessConsoleTelemetryTagListResponse> ListTagsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<DownstreamListResponse<DownstreamTelemetryTagListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/tags?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryTagListResponse(page.Items.Select(tag =>
            new BusinessConsoleTelemetryTagItem(
                FormatJsonScalar(tag.TelemetryTagId),
                tag.OrganizationId,
                tag.EnvironmentId,
                tag.DeviceAssetId,
                tag.TagKey,
                tag.ValueType,
                tag.UnitCode,
                tag.SamplingPolicy,
                tag.IsWritable,
                tag.ControlMinValue,
                tag.ControlMaxValue,
                tag.ControlAllowedValues ?? [])).ToArray(), page.Total);
    }

    public Task<BusinessConsoleTelemetryTagCurrentValueResponse> GetTagCurrentValueAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryTagCurrentValueRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryTagCurrentValueResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/tags/current-value?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("tagKey", request.TagKey)),
            null,
            cancellationToken);

    public async Task<BusinessConsoleTelemetryAlarmRuleListResponse> ListAlarmRulesAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmRuleListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<DownstreamListResponse<DownstreamAlarmRuleListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/alarm-rules?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("isEnabled", request.IsEnabled),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryAlarmRuleListResponse(page.Items.Select(rule =>
            new BusinessConsoleTelemetryAlarmRuleItem(
                FormatJsonScalar(rule.AlarmRuleId),
                rule.OrganizationId,
                rule.EnvironmentId,
                rule.DeviceAssetId,
                rule.RuleCode,
                rule.AlarmCode,
                rule.Severity,
                rule.TagKey,
                rule.ComparisonOperator,
                rule.ThresholdValue,
                rule.UnitCode,
                rule.IsEnabled,
                rule.UpdatedAtUtc)).ToArray(), page.Total);
    }

    public async Task<BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse> CreateOrUpdateAlarmRuleAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateAlarmRuleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/alarm-rules",
            request,
            cancellationToken);
        return new BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse(FormatJsonScalar(response.AlarmRuleId));
    }

    public async Task<BusinessConsoleTelemetryDeviceControlCommandResponse> CreateDeviceControlCommandAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandRequest request,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateDeviceControlCommandResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/device-control-commands",
            new DownstreamCreateDeviceControlCommandRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.DeviceAssetId,
                request.CommandType,
                request.TagKey,
                request.Value,
                request.Parameters,
                requestedBy,
                request.Reason,
                request.IdempotencyKey,
                request.CorrelationId),
            cancellationToken);
        return new BusinessConsoleTelemetryDeviceControlCommandResponse(
            response.OperationTaskId,
            response.Status,
            response.Approval is null
                ? null
                : new BusinessConsoleTelemetryOperationApprovalSummary(
                    response.Approval.Status,
                    response.Approval.RequestedBy,
                    response.Approval.RequestedAtUtc,
                    response.Approval.DecidedBy,
                    response.Approval.DecidedAtUtc,
                    response.Approval.DecisionReason));
    }

    public Task<BusinessConsoleTelemetryDeviceControlCommandDetail> GetDeviceControlCommandAsync(
        string internalBearerToken,
        string commandId,
        BusinessConsoleTelemetryDeviceControlCommandContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryDeviceControlCommandDetail>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/device-control-commands/{Uri.EscapeDataString(commandId)}?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId)),
            null,
            cancellationToken);

    public Task<BusinessConsoleTelemetryDeviceControlCommandListResponse> ListDeviceControlCommandsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlCommandListRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryDeviceControlCommandListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/device-control-commands?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("status", request.Status),
                ("fromUtc", request.FromUtc),
                ("toUtc", request.ToUtc),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public async Task<BusinessConsoleTelemetryDeviceControlBindingListResponse> ListDeviceControlBindingsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryDeviceControlBindingListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<DownstreamListResponse<DownstreamDeviceControlBindingListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/device-control-bindings?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("isActive", request.IsActive),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryDeviceControlBindingListResponse(page.Items.Select(binding =>
            new BusinessConsoleTelemetryDeviceControlBindingItem(
                FormatJsonScalar(binding.DeviceControlChannelBindingId),
                binding.OrganizationId,
                binding.EnvironmentId,
                binding.DeviceAssetId,
                binding.ConnectorHostId,
                binding.InstanceKey,
                binding.IsActive,
                binding.DisabledReason,
                binding.UpdatedAtUtc)).ToArray(), page.Total);
    }

    public async Task<BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingResponse> CreateOrUpdateDeviceControlBindingAsync(
        string internalBearerToken,
        BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateDeviceControlBindingResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/device-control-bindings",
            request,
            cancellationToken);
        return new BusinessConsoleCreateOrUpdateTelemetryDeviceControlBindingResponse(FormatJsonScalar(response.DeviceControlChannelBindingId));
    }

    public async Task<BusinessConsoleDisableTelemetryDeviceControlBindingResponse> DisableDeviceControlBindingAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleDisableTelemetryDeviceControlBindingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamCreateOrUpdateDeviceControlBindingResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/iiot/device-control-bindings/{Uri.EscapeDataString(deviceAssetId)}/disable",
            request,
            cancellationToken);
        return new BusinessConsoleDisableTelemetryDeviceControlBindingResponse(FormatJsonScalar(response.DeviceControlChannelBindingId));
    }

    public async Task<BusinessConsoleRecordTelemetrySampleResponse> RecordSampleAsync(
        string internalBearerToken,
        BusinessConsoleRecordTelemetrySampleRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamRecordTelemetrySampleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/samples",
            request,
            cancellationToken);
        return new BusinessConsoleRecordTelemetrySampleResponse(
            FormatOptionalJsonScalar(response.TelemetrySummaryId),
            FormatOptionalJsonScalar(response.DeviceStateSnapshotId));
    }

    public async Task<BusinessConsolePostTelemetryAlarmResponse> PostAlarmAsync(
        string internalBearerToken,
        BusinessConsolePostTelemetryAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamPostTelemetryAlarmResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/iiot/alarms",
            request,
            cancellationToken);
        return new BusinessConsolePostTelemetryAlarmResponse(FormatJsonScalar(response.AlarmEventId));
    }

    public async Task<BusinessConsoleTelemetryAlarmEventListResponse> ListAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryAlarmListRequest request,
        CancellationToken cancellationToken)
    {
        var page = await SendAsync<DownstreamListResponse<DownstreamAlarmEventListItem>>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/alarms?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("deviceAssetIds", request.DeviceAssetIds),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take),
                ("alarmEventId", request.AlarmEventId)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryAlarmEventListResponse(page.Items.Select(alarm =>
            new BusinessConsoleTelemetryAlarmEventItem(
                FormatJsonScalar(alarm.AlarmEventId),
                alarm.OrganizationId,
                alarm.EnvironmentId,
                alarm.DeviceAssetId,
                alarm.AlarmCode,
                alarm.Severity,
                alarm.Status,
                alarm.RaisedAtUtc,
                alarm.ClearedAtUtc,
                alarm.ExternalAlarmId,
                alarm.AcknowledgedAtUtc,
                alarm.AcknowledgedBy,
                alarm.ShelvedAtUtc,
                alarm.ShelvedUntilUtc,
                alarm.ShelvedBy,
                alarm.ShelveReason,
                alarm.EscalatedAtUtc,
                alarm.EscalationReason,
                alarm.EscalationRecipientRefs)).ToArray(), page.Total);
    }

    public async Task<BusinessConsoleTelemetryHistoryResponse> QueryHistoryAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleTelemetryHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var items = await SendAsync<IReadOnlyCollection<BusinessConsoleTelemetryHistoryItem>>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/devices/{Uri.EscapeDataString(deviceAssetId)}/timeline?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("fromUtc", request.FromUtc),
                ("toUtc", request.ToUtc)),
            null,
            cancellationToken);
        return new BusinessConsoleTelemetryHistoryResponse(items);
    }

    public Task<BusinessConsoleTelemetryOeeResponse> QueryOeeAsync(
        string internalBearerToken,
        BusinessConsoleTelemetryOeeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryOeeResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/oee?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("deviceAssetId", request.DeviceAssetId),
                ("windowStartUtc", request.WindowStartUtc),
                ("windowEndUtc", request.WindowEndUtc)),
            null,
            cancellationToken);

    public async Task<BusinessOeeAggregateResponse> QueryOeeAggregatesAsync(
        string internalBearerToken,
        BusinessOeeAggregateRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessOeeAggregateResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/oee/aggregates?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("dimension", OeeDimension(request.Dimension)),
                ("windowStartUtc", request.WindowStartUtc),
                ("windowEndUtc", request.WindowEndUtc),
                ("deviceAssetId", request.DeviceAssetId),
                ("workCenterId", request.WorkCenterId),
                ("shiftCode", request.ShiftCode),
                ("lineCode", request.LineCode),
                ("workshopCode", request.WorkshopCode),
                ("businessDate", request.BusinessDate),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken,
            failClosedOnFailureEnvelope: true);
        if (!string.Equals(response.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(response.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal)
            || response.Dimension != request.Dimension
            || response.WindowStartUtc != request.WindowStartUtc
            || response.WindowEndUtc != request.WindowEndUtc
            || response.Buckets is null
            || response.TotalCount < response.Buckets.Count
            || response.Skip != request.Skip
            || response.Take != request.Take
            || response.Buckets.Any(bucket =>
                bucket is null
                || bucket.Dimension != request.Dimension
                || bucket.BucketEndUtc <= bucket.BucketStartUtc
                || bucket.BucketStartUtc < request.WindowStartUtc
                || bucket.BucketEndUtc > request.WindowEndUtc
                || bucket.DeviceCount < 0
                || bucket.StateSampleCount < 0
                || bucket.ProductionFactCount < 0
                || bucket.DegradedReasons is null
                || !BucketMatchesRequest(bucket, request)))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }

        return response;
    }

    private static bool BucketMatchesRequest(
        BusinessOeeAggregateBucket bucket,
        BusinessOeeAggregateRequest request) =>
        MatchesRequested(bucket.DeviceAssetId, request.DeviceAssetId)
        && MatchesRequested(bucket.WorkCenterId, request.WorkCenterId)
        && MatchesRequested(bucket.ShiftCode, request.ShiftCode)
        && MatchesRequested(bucket.LineCode, request.LineCode)
        && MatchesRequested(bucket.WorkshopCode, request.WorkshopCode)
        && (request.BusinessDate is null || bucket.BusinessDate == request.BusinessDate)
        && (request.Dimension != BusinessOeeAggregateDimension.Device
            || request.DeviceAssetId is null
            || string.Equals(bucket.DeviceAssetId, request.DeviceAssetId, StringComparison.Ordinal))
        && (request.Dimension != BusinessOeeAggregateDimension.WorkCenter
            || request.WorkCenterId is null
            || string.Equals(bucket.WorkCenterId, request.WorkCenterId, StringComparison.Ordinal))
        && (request.Dimension != BusinessOeeAggregateDimension.Line
            || request.LineCode is null
            || string.Equals(bucket.LineCode, request.LineCode, StringComparison.Ordinal))
        && (request.Dimension != BusinessOeeAggregateDimension.Workshop
            || request.WorkshopCode is null
            || string.Equals(bucket.WorkshopCode, request.WorkshopCode, StringComparison.Ordinal))
        && (request.Dimension != BusinessOeeAggregateDimension.Shift
            || request.ShiftCode is null
            || string.Equals(bucket.ShiftCode, request.ShiftCode, StringComparison.Ordinal))
        && (request.Dimension != BusinessOeeAggregateDimension.Day
            || request.BusinessDate is null
            || bucket.BusinessDate == request.BusinessDate);

    private static bool MatchesRequested(string? actual, string? requested) =>
        requested is null || string.Equals(actual, requested, StringComparison.Ordinal);

    public Task<EquipmentRuntimeAvailabilityResponse> GetRuntimeAvailabilityAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/iiot/runtime-availability?" + AvailabilityQuery(request),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public Task<BusinessConsoleTelemetryRuntimeHoursResponse> QueryRuntimeHoursAsync(string internalBearerToken, BusinessConsoleTelemetryRuntimeHoursRequest request, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleTelemetryRuntimeHoursResponse>(internalBearerToken, HttpMethod.Get,
            "/api/business/v1/iiot/runtime-hours?" + Query(("organizationId", request.OrganizationId), ("environmentId", request.EnvironmentId), ("deviceAssetId", request.DeviceAssetId), ("windowStartUtc", request.WindowStartUtc), ("windowEndUtc", request.WindowEndUtc)),
            null, cancellationToken);

    public Task<EquipmentRuntimeAvailabilityResponse> GetDeviceRuntimeAvailabilityAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeAvailabilityResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/devices/{Uri.EscapeDataString(deviceAssetId)}/runtime-availability?" + DeviceAvailabilityQuery(request),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public Task<EquipmentRuntimeCurrentStateResponse> GetDeviceCurrentStateAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<EquipmentRuntimeCurrentStateResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/devices/{Uri.EscapeDataString(deviceAssetId)}/current-state?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            EquipmentRuntimeJson.Options);

    public async Task<BusinessConsoleEquipmentHealthResponse> GetEquipmentHealthAsync(
        string internalBearerToken,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<BusinessConsoleEquipmentHealthResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/iiot/devices/{Uri.EscapeDataString(deviceAssetId)}/health?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken,
            EquipmentHealthJsonOptions);

        ValidateEquipmentHealthResponse(response, deviceAssetId, request);
        return response;
    }

    private static void ValidateEquipmentHealthResponse(
        BusinessConsoleEquipmentHealthResponse response,
        string deviceAssetId,
        BusinessConsoleEquipmentContextRequest request)
    {
        if (!string.Equals(response.OrganizationId, request.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(response.EnvironmentId, request.EnvironmentId, StringComparison.Ordinal)
            || !string.Equals(response.DeviceAssetId, deviceAssetId, StringComparison.Ordinal)
            || response.HealthScore is < 0 or > 100
            || !Enum.IsDefined(response.Level)
            || response.CalculatedAtUtc == default
            || response.CalculatedAtUtc.Offset != TimeSpan.Zero
            || response.DataFreshness is null
            || response.RiskFactors is null
            || response.RuleEvaluations is null)
        {
            throw InvalidEquipmentHealthResponse();
        }

        if (response.RuleEvaluations.Count != EquipmentHealthRuleCodes.Count
            || response.RuleEvaluations.Any(evaluation => evaluation is null)
            || !EquipmentHealthRuleCodes.SetEquals(response.RuleEvaluations.Select(evaluation => evaluation.RuleCode)))
        {
            throw InvalidEquipmentHealthResponse();
        }

        foreach (var evaluation in response.RuleEvaluations)
        {
            if (!HasRequiredRuleFields(
                    evaluation.RuleCode,
                    evaluation.RuleName,
                    evaluation.CurrentValue,
                    evaluation.Threshold,
                    evaluation.Unit,
                    evaluation.Evidence)
                || !Enum.IsDefined(evaluation.Status)
                || !HasCanonicalPenalty(evaluation.RuleCode, evaluation.Status, evaluation.Penalty)
                || !HasCoherentEvidenceSource(
                    evaluation.SourceFactType,
                    evaluation.SourceFactLabel,
                    evaluation.SourceFactOccurredAtUtc))
            {
                throw InvalidEquipmentHealthResponse();
            }
        }

        if (response.RiskFactors.Any(factor => factor is null))
        {
            throw InvalidEquipmentHealthResponse();
        }

        var riskEvaluations = response.RuleEvaluations
            .Where(evaluation => evaluation.Status == BusinessConsoleEquipmentHealthRuleStatus.Risk)
            .ToDictionary(evaluation => evaluation.RuleCode, StringComparer.Ordinal);
        var riskFactorCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var factor in response.RiskFactors)
        {
            if (!HasRequiredRuleFields(
                    factor.RuleCode,
                    factor.RuleName,
                    factor.CurrentValue,
                    factor.Threshold,
                    factor.Unit,
                    factor.Evidence)
                || !Enum.IsDefined(factor.Status)
                || factor.Status != BusinessConsoleEquipmentHealthRuleStatus.Risk
                || factor.Penalty <= 0
                || !HasCoherentEvidenceSource(
                    factor.SourceFactType,
                    factor.SourceFactLabel,
                    factor.SourceFactOccurredAtUtc)
                || !riskFactorCodes.Add(factor.RuleCode)
                || !riskEvaluations.TryGetValue(factor.RuleCode, out var evaluation)
                || !RiskFactorMatchesEvaluation(factor, evaluation))
            {
                throw InvalidEquipmentHealthResponse();
            }
        }

        var expectedScore = Math.Clamp(
            100 - riskEvaluations.Values.Sum(evaluation => evaluation.Penalty),
            0,
            100);
        if (!riskFactorCodes.SetEquals(riskEvaluations.Keys)
            || response.HealthScore != expectedScore
            || response.Level != ClassifyEquipmentHealth(expectedScore)
            || !HasCoherentFreshness(
                response.DataFreshness,
                response.CalculatedAtUtc,
                response.RuleEvaluations))
        {
            throw InvalidEquipmentHealthResponse();
        }
    }

    private static bool HasRequiredRuleFields(
        string ruleCode,
        string ruleName,
        string currentValue,
        string threshold,
        string unit,
        string evidence) =>
        !string.IsNullOrWhiteSpace(ruleCode)
        && !string.IsNullOrWhiteSpace(ruleName)
        && !string.IsNullOrWhiteSpace(currentValue)
        && !string.IsNullOrWhiteSpace(threshold)
        && !string.IsNullOrWhiteSpace(unit)
        && !string.IsNullOrWhiteSpace(evidence);

    private static bool HasCanonicalPenalty(
        string ruleCode,
        BusinessConsoleEquipmentHealthRuleStatus status,
        int penalty)
    {
        if (status != BusinessConsoleEquipmentHealthRuleStatus.Risk)
        {
            return penalty == 0;
        }

        return ruleCode switch
        {
            "threshold-proximity" => penalty == 15,
            "runtime-hours-24h" => penalty == 10,
            "alarm-frequency-24h" => penalty is 20 or 45 or 65,
            "sustained-exceedance" => penalty == 20,
            "trend-growth" => penalty == 15,
            _ => false,
        };
    }

    private static BusinessConsoleEquipmentHealthLevel ClassifyEquipmentHealth(int score) =>
        score switch
        {
            >= 90 => BusinessConsoleEquipmentHealthLevel.Healthy,
            >= 70 => BusinessConsoleEquipmentHealthLevel.Watch,
            >= 40 => BusinessConsoleEquipmentHealthLevel.Warning,
            _ => BusinessConsoleEquipmentHealthLevel.Critical,
        };

    private static bool HasCoherentEvidenceSource(
        string? sourceFactType,
        string? sourceFactLabel,
        DateTimeOffset? sourceFactOccurredAtUtc)
    {
        if (sourceFactOccurredAtUtc is null)
        {
            return sourceFactType is null && sourceFactLabel is null;
        }

        return sourceFactOccurredAtUtc.Value != default
            && sourceFactOccurredAtUtc.Value.Offset == TimeSpan.Zero
            && !string.IsNullOrWhiteSpace(sourceFactType)
            && !string.IsNullOrWhiteSpace(sourceFactLabel);
    }

    private static bool RiskFactorMatchesEvaluation(
        BusinessConsoleEquipmentHealthRiskFactor factor,
        BusinessConsoleEquipmentHealthRuleEvaluation evaluation) =>
        factor.RuleName == evaluation.RuleName
        && factor.Status == evaluation.Status
        && factor.Penalty == evaluation.Penalty
        && factor.CurrentValue == evaluation.CurrentValue
        && factor.Threshold == evaluation.Threshold
        && factor.Unit == evaluation.Unit
        && factor.Evidence == evaluation.Evidence
        && factor.SourceFactType == evaluation.SourceFactType
        && factor.SourceFactLabel == evaluation.SourceFactLabel
        && factor.SourceFactOccurredAtUtc == evaluation.SourceFactOccurredAtUtc;

    private static bool HasCoherentFreshness(
        BusinessConsoleEquipmentHealthDataFreshness freshness,
        DateTimeOffset calculatedAtUtc,
        IReadOnlyCollection<BusinessConsoleEquipmentHealthRuleEvaluation> evaluations)
    {
        if (!Enum.IsDefined(freshness.Status))
        {
            return false;
        }

        if (freshness.Status == BusinessConsoleEquipmentHealthFreshness.Unavailable)
        {
            return freshness.AgeSeconds is null
                && freshness.LatestFactAtUtc is null
                && freshness.SourceFactType is null
                && freshness.SourceFactLabel is null
                && evaluations.All(evaluation => evaluation.SourceFactOccurredAtUtc is null);
        }

        if (freshness.AgeSeconds is null or < 0
            || freshness.LatestFactAtUtc is null
            || freshness.LatestFactAtUtc.Value == default
            || freshness.LatestFactAtUtc.Value.Offset != TimeSpan.Zero
            || freshness.LatestFactAtUtc > calculatedAtUtc
            || string.IsNullOrWhiteSpace(freshness.SourceFactType)
            || string.IsNullOrWhiteSpace(freshness.SourceFactLabel))
        {
            return false;
        }

        var exactAge = calculatedAtUtc - freshness.LatestFactAtUtc.Value;
        var ageSeconds = (long)exactAge.TotalSeconds;
        var expectedStatus = exactAge <= TimeSpan.FromMinutes(2)
            ? BusinessConsoleEquipmentHealthFreshness.Fresh
            : exactAge <= TimeSpan.FromMinutes(10)
                ? BusinessConsoleEquipmentHealthFreshness.Delayed
                : BusinessConsoleEquipmentHealthFreshness.Stale;
        return ageSeconds == freshness.AgeSeconds.Value
            && freshness.Status == expectedStatus;
    }

    private static BusinessServiceProxyException InvalidEquipmentHealthResponse() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(
            HttpStatusCode.BadGateway,
            "downstream-invalid-response");

    public async Task<BusinessConsoleEquipmentAlarmListPageResponse> ListActiveAlarmsAsync(
        string internalBearerToken,
        BusinessConsoleEquipmentAlarmListRequest request,
        CancellationToken cancellationToken)
    {
        var alarms = await ListAlarmsAsync(
            internalBearerToken,
            new BusinessConsoleTelemetryAlarmListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.DeviceAssetId,
                request.Status ?? "active",
                request.Skip,
                request.Take,
                request.DeviceAssetIds,
                request.AlarmEventId),
            cancellationToken);
        return new BusinessConsoleEquipmentAlarmListPageResponse(
            alarms.Items,
            alarms.Total);
    }

    public async Task<BusinessConsoleAlarmLifecycleResponse> AcknowledgeAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleAcknowledgeAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamAlarmLifecycleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/iiot/alarms/{Uri.EscapeDataString(alarmEventId)}/acknowledge",
            request,
            cancellationToken);
        var resourceId = FormatJsonScalar(response.AlarmEventId);
        EnsureRouteResource(resourceId, alarmEventId);
        return new BusinessConsoleAlarmLifecycleResponse(
            resourceId,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Accepted(
                    "iiot.alarm.acknowledge",
                    "industrial-telemetry",
                    "alarm-event",
                    resourceId,
                    AlarmReadbackPath(request.OrganizationId, request.EnvironmentId, resourceId),
                    request.IdempotencyKey));
    }

    public async Task<BusinessConsoleAlarmLifecycleResponse> ShelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleShelveAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamAlarmLifecycleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/iiot/alarms/{Uri.EscapeDataString(alarmEventId)}/shelve",
            request,
            cancellationToken);
        var resourceId = FormatJsonScalar(response.AlarmEventId);
        EnsureRouteResource(resourceId, alarmEventId);
        return new BusinessConsoleAlarmLifecycleResponse(
            resourceId,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Accepted(
                    "iiot.alarm.shelve",
                    "industrial-telemetry",
                    "alarm-event",
                    resourceId,
                    AlarmReadbackPath(request.OrganizationId, request.EnvironmentId, resourceId),
                    request.IdempotencyKey));
    }

    public async Task<BusinessConsoleAlarmLifecycleResponse> UnshelveAlarmAsync(
        string internalBearerToken,
        string alarmEventId,
        BusinessConsoleUnshelveAlarmRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DownstreamAlarmLifecycleResponse>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/iiot/alarms/{Uri.EscapeDataString(alarmEventId)}/unshelve",
            request,
            cancellationToken);
        var resourceId = FormatJsonScalar(response.AlarmEventId);
        EnsureRouteResource(resourceId, alarmEventId);
        return new BusinessConsoleAlarmLifecycleResponse(
            resourceId,
            string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? null
                : BusinessConsoleOperationReceipts.Accepted(
                    "iiot.alarm.unshelve",
                    "industrial-telemetry",
                    "alarm-event",
                    resourceId,
                    AlarmReadbackPath(request.OrganizationId, request.EnvironmentId, resourceId),
                    request.IdempotencyKey));
    }

    private static string AlarmReadbackPath(string organizationId, string environmentId, string alarmEventId) =>
        $"/api/business-console/v1/equipment/alarms?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}&alarmEventId={Uri.EscapeDataString(alarmEventId)}";

    private static void EnsureRouteResource(string downstreamResourceId, string routeResourceId)
    {
        if (string.IsNullOrWhiteSpace(downstreamResourceId)
            || !string.Equals(downstreamResourceId, routeResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.BadGateway,
                "downstream-invalid-response");
        }
    }

    private static string AvailabilityQuery(BusinessConsoleEquipmentAvailabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc),
            ("deviceAssetIds", request.DeviceAssetIds),
            ("workCenterIds", request.WorkCenterIds));

    private static string DeviceAvailabilityQuery(BusinessConsoleEquipmentAvailabilityRequest request) =>
        Query(
            ("organizationId", request.OrganizationId),
            ("environmentId", request.EnvironmentId),
            ("windowStartUtc", request.WindowStartUtc),
            ("windowEndUtc", request.WindowEndUtc));

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private static string OeeDimension(BusinessOeeAggregateDimension dimension) => dimension switch
    {
        BusinessOeeAggregateDimension.Device => "device",
        BusinessOeeAggregateDimension.WorkCenter => "workCenter",
        BusinessOeeAggregateDimension.Line => "line",
        BusinessOeeAggregateDimension.Workshop => "workshop",
        BusinessOeeAggregateDimension.Shift => "shift",
        BusinessOeeAggregateDimension.Day => "day",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    private static string FormatJsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => value.ToString(),
    };

    private static string? FormatOptionalJsonScalar(JsonElement? value) =>
        value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : FormatJsonScalar(value.Value);

    private sealed record DownstreamListResponse<T>(IReadOnlyCollection<T> Items, int Total);

    private sealed record DownstreamAlarmEventListItem(
        JsonElement AlarmEventId,
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string AlarmCode,
        string Severity,
        string Status,
        DateTimeOffset RaisedAtUtc,
        DateTimeOffset? ClearedAtUtc,
        string ExternalAlarmId,
        DateTimeOffset? AcknowledgedAtUtc = null,
        string? AcknowledgedBy = null,
        DateTimeOffset? ShelvedAtUtc = null,
        DateTimeOffset? ShelvedUntilUtc = null,
        string? ShelvedBy = null,
        string? ShelveReason = null,
        DateTimeOffset? EscalatedAtUtc = null,
        string? EscalationReason = null,
        IReadOnlyCollection<string>? EscalationRecipientRefs = null);

    private sealed record DownstreamTelemetryTagListItem(
        JsonElement TelemetryTagId,
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

    private sealed record DownstreamAlarmRuleListItem(
        JsonElement AlarmRuleId,
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

    private sealed record DownstreamCreateOrUpdateAlarmRuleResponse(JsonElement AlarmRuleId);

    private sealed record DownstreamRecordTelemetrySampleResponse(
        JsonElement? TelemetrySummaryId,
        JsonElement? DeviceStateSnapshotId);

    private sealed record DownstreamPostTelemetryAlarmResponse(JsonElement AlarmEventId);

    private sealed record DownstreamAlarmLifecycleResponse(JsonElement AlarmEventId);

    private sealed record DownstreamCreateDeviceControlCommandRequest(
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

    private sealed record DownstreamCreateDeviceControlCommandResponse(
        string OperationTaskId,
        string Status,
        DownstreamOperationApprovalSummary? Approval);

    private sealed record DownstreamOperationApprovalSummary(
        string Status,
        string RequestedBy,
        DateTimeOffset RequestedAtUtc,
        string? DecidedBy,
        DateTimeOffset? DecidedAtUtc,
        string? DecisionReason);

    private sealed record DownstreamDeviceControlBindingListItem(
        JsonElement DeviceControlChannelBindingId,
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        string ConnectorHostId,
        string InstanceKey,
        bool IsActive,
        string? DisabledReason,
        DateTimeOffset UpdatedAtUtc);

    private sealed record DownstreamCreateOrUpdateDeviceControlBindingResponse(JsonElement DeviceControlChannelBindingId);
}
