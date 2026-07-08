using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Telemetry;

public abstract class AuthorizedBusinessTelemetryRuntimeProxyEndpoint<TRequest, TResponse>(
    IBusinessGatewayAuthorizationClient auth,
    string permissionCode)
    : AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(auth, permissionCode)
    where TRequest : notnull
{
    protected override JsonSerializerOptions? ResponseJsonOptions => EquipmentRuntimeJson.Options;
}

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/tags")]
[BusinessGatewayOperationId("listBusinessConsoleTelemetryTags")]
public sealed class ListBusinessConsoleTelemetryTagsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleTelemetryTagListRequest, BusinessConsoleTelemetryTagListResponse>(
        auth,
        BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleTelemetryTagListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleTelemetryTagListRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleTelemetryTagListRequest request) => request.DeviceAssetId is null ? null : "device-asset";

    protected override string? ResourceId(BusinessConsoleTelemetryTagListRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleTelemetryTagListResponse> ForwardAsync(
        BusinessConsoleTelemetryTagListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.ListTagsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/alarm-rules")]
[BusinessGatewayOperationId("listBusinessConsoleTelemetryAlarmRules")]
public sealed class ListBusinessConsoleTelemetryAlarmRulesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleTelemetryAlarmRuleListRequest, BusinessConsoleTelemetryAlarmRuleListResponse>(
        auth,
        BusinessGatewayPermissions.IiotAlarmsRead)
{
    protected override string OrganizationId(BusinessConsoleTelemetryAlarmRuleListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleTelemetryAlarmRuleListRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleTelemetryAlarmRuleListRequest request) => request.DeviceAssetId is null ? null : "device-asset";

    protected override string? ResourceId(BusinessConsoleTelemetryAlarmRuleListRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleTelemetryAlarmRuleListResponse> ForwardAsync(
        BusinessConsoleTelemetryAlarmRuleListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.ListAlarmRulesAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpPost("/api/business-console/v1/telemetry/alarm-rules")]
[BusinessGatewayOperationId("createOrUpdateBusinessConsoleTelemetryAlarmRule")]
public sealed class CreateOrUpdateBusinessConsoleTelemetryAlarmRuleEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest, BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse>(
        auth,
        BusinessGatewayPermissions.IiotAlarmRulesManage)
{
    protected override string OrganizationId(BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request) => "device-asset";

    protected override string ResourceId(BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleCreateOrUpdateTelemetryAlarmRuleResponse> ForwardAsync(
        BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.CreateOrUpdateAlarmRuleAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpPost("/api/business-console/v1/telemetry/device-control-commands")]
[BusinessGatewayOperationId("createBusinessConsoleTelemetryDeviceControlCommand")]
public sealed class CreateBusinessConsoleTelemetryDeviceControlCommandEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleTelemetryDeviceControlCommandRequest, BusinessConsoleTelemetryDeviceControlCommandResponse>(
        auth,
        BusinessGatewayPermissions.IiotDeviceControlWrite)
{
    protected override string OrganizationId(BusinessConsoleTelemetryDeviceControlCommandRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleTelemetryDeviceControlCommandRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleTelemetryDeviceControlCommandRequest request) => "device-asset";

    protected override string ResourceId(BusinessConsoleTelemetryDeviceControlCommandRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleTelemetryDeviceControlCommandResponse> ForwardAsync(
        BusinessConsoleTelemetryDeviceControlCommandRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        // Inject the authenticated principal as the command requester so the audit actor
        // is bound to the caller identity rather than an attacker-supplied field.
        var (_, actorRef) = RequireAuthorizedPrincipalActor();
        return telemetry.CreateDeviceControlCommandAsync(tokenProvider.BearerToken, request, actorRef, cancellationToken);
    }
}

[Tags("Business Console Telemetry")]
[HttpPost("/api/business-console/v1/telemetry/samples")]
[BusinessGatewayOperationId("recordBusinessConsoleTelemetrySample")]
public sealed class RecordBusinessConsoleTelemetrySampleEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordTelemetrySampleRequest, BusinessConsoleRecordTelemetrySampleResponse>(
        auth,
        BusinessGatewayPermissions.IiotTelemetryWrite)
{
    protected override string OrganizationId(BusinessConsoleRecordTelemetrySampleRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRecordTelemetrySampleRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleRecordTelemetrySampleRequest request) => "device-asset";

    protected override string? ResourceId(BusinessConsoleRecordTelemetrySampleRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleRecordTelemetrySampleResponse> ForwardAsync(
        BusinessConsoleRecordTelemetrySampleRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.RecordSampleAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpPost("/api/business-console/v1/telemetry/alarms")]
[BusinessGatewayOperationId("postBusinessConsoleTelemetryAlarm")]
public sealed class PostBusinessConsoleTelemetryAlarmEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsolePostTelemetryAlarmRequest, BusinessConsolePostTelemetryAlarmResponse>(
        auth,
        BusinessGatewayPermissions.IiotAlarmsWrite)
{
    protected override string OrganizationId(BusinessConsolePostTelemetryAlarmRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsolePostTelemetryAlarmRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsolePostTelemetryAlarmRequest request) => "device-asset";

    protected override string? ResourceId(BusinessConsolePostTelemetryAlarmRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsolePostTelemetryAlarmResponse> ForwardAsync(
        BusinessConsolePostTelemetryAlarmRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.PostAlarmAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/alarms")]
[BusinessGatewayOperationId("listBusinessConsoleTelemetryAlarms")]
public sealed class ListBusinessConsoleTelemetryAlarmsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    BusinessGatewayDataScopeFilter dataScopeFilter,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleTelemetryAlarmListRequest, BusinessConsoleTelemetryAlarmEventListResponse>(
        auth,
        BusinessGatewayPermissions.IiotAlarmsRead)
{
    protected override string OrganizationId(BusinessConsoleTelemetryAlarmListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleTelemetryAlarmListRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleTelemetryAlarmListRequest request) => request.DeviceAssetId is null ? null : "device-asset";

    protected override string? ResourceId(BusinessConsoleTelemetryAlarmListRequest request) => request.DeviceAssetId;

    protected override async Task<BusinessConsoleTelemetryAlarmEventListResponse> ForwardAsync(
        BusinessConsoleTelemetryAlarmListRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var scopedRequest = await dataScopeFilter.ApplyToTelemetryAlarmsAsync(
            request,
            AuthorizationResult?.DataScope,
            cancellationToken);
        return await telemetry.ListAlarmsAsync(tokenProvider.BearerToken, scopedRequest, cancellationToken);
    }
}

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/devices/{deviceAssetId}/history")]
[BusinessGatewayOperationId("queryBusinessConsoleTelemetryDeviceHistory")]
public sealed class QueryBusinessConsoleTelemetryDeviceHistoryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleTelemetryHistoryRequest, BusinessConsoleTelemetryHistoryResponse>(
        auth,
        BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleTelemetryHistoryRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleTelemetryHistoryRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleTelemetryHistoryRequest request) => "device-asset";

    protected override string? ResourceId(BusinessConsoleTelemetryHistoryRequest request) => Route<string>("deviceAssetId");

    protected override Task<BusinessConsoleTelemetryHistoryResponse> ForwardAsync(
        BusinessConsoleTelemetryHistoryRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.QueryHistoryAsync(tokenProvider.BearerToken, Route<string>("deviceAssetId")!, request, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/oee")]
[BusinessGatewayOperationId("queryBusinessConsoleTelemetryOee")]
public sealed class QueryBusinessConsoleTelemetryOeeEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleTelemetryOeeRequest, BusinessConsoleTelemetryOeeResponse>(
        auth,
        BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleTelemetryOeeRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleTelemetryOeeRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleTelemetryOeeRequest request) => "device-asset";

    protected override string ResourceId(BusinessConsoleTelemetryOeeRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleTelemetryOeeResponse> ForwardAsync(
        BusinessConsoleTelemetryOeeRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.QueryOeeAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Telemetry")]
[HttpGet("/api/business-console/v1/telemetry/runtime-availability")]
[BusinessGatewayOperationId("queryBusinessConsoleTelemetryRuntimeAvailability")]
public sealed class QueryBusinessConsoleTelemetryRuntimeAvailabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessTelemetryRuntimeProxyEndpoint<BusinessConsoleEquipmentAvailabilityRequest, EquipmentRuntimeAvailabilityResponse>(
        auth,
        BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleEquipmentAvailabilityRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleEquipmentAvailabilityRequest request) => request.EnvironmentId;

    protected override Task<EquipmentRuntimeAvailabilityResponse> ForwardAsync(
        BusinessConsoleEquipmentAvailabilityRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.GetRuntimeAvailabilityAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleTelemetryTagListRequestValidator : Validator<BusinessConsoleTelemetryTagListRequest>
{
    public BusinessConsoleTelemetryTagListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleTelemetryAlarmListRequestValidator : Validator<BusinessConsoleTelemetryAlarmListRequest>
{
    public BusinessConsoleTelemetryAlarmListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).MaximumLength(150);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleTelemetryAlarmRuleListRequestValidator : Validator<BusinessConsoleTelemetryAlarmRuleListRequest>
{
    public BusinessConsoleTelemetryAlarmRuleListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequestValidator : Validator<BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest>
{
    // Gateway must mirror the IndustrialTelemetry AlarmRule operator contract without referencing service internals.
    private static readonly HashSet<string> SupportedOperators = new(StringComparer.Ordinal)
    {
        ">",
        ">=",
        "<",
        "<=",
        "==",
        "!=",
    };

    public BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RuleCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AlarmCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Severity).NotEmpty().MaximumLength(50);
        RuleFor(x => x.TagKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ComparisonOperator)
            .NotEmpty()
            .MaximumLength(8)
            .Must(IsSupportedOperator)
            .WithMessage("Unsupported alarm rule comparison operator.");
        RuleFor(x => x.UnitCode).NotEmpty().MaximumLength(50);
    }

    private static bool IsSupportedOperator(string comparisonOperator)
    {
        return SupportedOperators.Contains(comparisonOperator);
    }
}

public sealed class BusinessConsoleTelemetryDeviceControlCommandRequestValidator
    : Validator<BusinessConsoleTelemetryDeviceControlCommandRequest>
{
    // Gateway mirrors the IndustrialTelemetry device-control command-type contract without
    // referencing service internals. RequestedBy is not validated here: the gateway injects
    // the authenticated principal as the requester before forwarding downstream.
    public BusinessConsoleTelemetryDeviceControlCommandRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ConnectorHostId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.InstanceKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CommandType)
            .NotEmpty()
            .MaximumLength(50)
            .Must(IsSupportedCommandType)
            .WithMessage("Device control command type must be write-tag, start-stop or parameter-set.");
        When(x => IsSingleTagCommand(x.CommandType), () =>
        {
            RuleFor(x => x.TagKey).NotEmpty().MaximumLength(150);
            RuleFor(x => x.Value).NotEmpty().MaximumLength(256);
        });
        When(x => IsParameterSetCommand(x.CommandType), () =>
        {
            RuleFor(x => x.Parameters).NotEmpty();
            RuleForEach(x => x.Parameters!.Keys).NotEmpty().MaximumLength(150);
            RuleForEach(x => x.Parameters!.Values).NotEmpty().MaximumLength(256);
        });
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(150);
    }

    private static bool IsSingleTagCommand(string commandType)
    {
        if (string.IsNullOrWhiteSpace(commandType))
        {
            return false;
        }

        var normalized = commandType.Trim().ToLowerInvariant();
        return normalized is "write-tag" or "start-stop";
    }

    private static bool IsParameterSetCommand(string commandType) =>
        !string.IsNullOrWhiteSpace(commandType) &&
        string.Equals(commandType.Trim(), "parameter-set", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedCommandType(string commandType) =>
        IsSingleTagCommand(commandType) || IsParameterSetCommand(commandType);
}

public sealed class BusinessConsoleTelemetryHistoryRequestValidator : Validator<BusinessConsoleTelemetryHistoryRequest>
{
    public BusinessConsoleTelemetryHistoryRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ToUtc).GreaterThan(x => x.FromUtc).When(x => x.FromUtc is not null && x.ToUtc is not null);
    }
}

public sealed class BusinessConsoleRecordTelemetrySampleRequestValidator
    : Validator<BusinessConsoleRecordTelemetrySampleRequest>
{
    public BusinessConsoleRecordTelemetrySampleRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.TagKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BucketEndUtc).GreaterThan(x => x.BucketStartUtc);
        RuleFor(x => x.SampleCount).GreaterThan(0);
        RuleFor(x => x.SourceSequence).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SourceSystem).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceConnector).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceState).MaximumLength(50);
    }
}

public sealed class BusinessConsolePostTelemetryAlarmRequestValidator
    : Validator<BusinessConsolePostTelemetryAlarmRequest>
{
    public BusinessConsolePostTelemetryAlarmRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AlarmCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Severity).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ExternalAlarmId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ClearedBy).MaximumLength(150);
        RuleFor(x => x.ClearReason).MaximumLength(300);
    }
}

public sealed class BusinessConsoleTelemetryOeeRequestValidator : Validator<BusinessConsoleTelemetryOeeRequest>
{
    public BusinessConsoleTelemetryOeeRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
    }
}
