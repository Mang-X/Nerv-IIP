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
[HttpGet("/api/business-console/v1/telemetry/alarms")]
[BusinessGatewayOperationId("listBusinessConsoleTelemetryAlarms")]
public sealed class ListBusinessConsoleTelemetryAlarmsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient telemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleTelemetryAlarmListRequest, BusinessConsoleTelemetryAlarmEventListResponse>(
        auth,
        BusinessGatewayPermissions.IiotAlarmsRead)
{
    protected override string OrganizationId(BusinessConsoleTelemetryAlarmListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleTelemetryAlarmListRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleTelemetryAlarmListRequest request) => request.DeviceAssetId is null ? null : "device-asset";

    protected override string? ResourceId(BusinessConsoleTelemetryAlarmListRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleTelemetryAlarmEventListResponse> ForwardAsync(
        BusinessConsoleTelemetryAlarmListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        telemetry.ListAlarmsAsync(tokenProvider.BearerToken, request, cancellationToken);
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
    }
}

public sealed class BusinessConsoleTelemetryAlarmRuleListRequestValidator : Validator<BusinessConsoleTelemetryAlarmRuleListRequest>
{
    public BusinessConsoleTelemetryAlarmRuleListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).MaximumLength(150);
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

public sealed class BusinessConsoleTelemetryHistoryRequestValidator : Validator<BusinessConsoleTelemetryHistoryRequest>
{
    public BusinessConsoleTelemetryHistoryRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ToUtc).GreaterThan(x => x.FromUtc).When(x => x.FromUtc is not null && x.ToUtc is not null);
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
