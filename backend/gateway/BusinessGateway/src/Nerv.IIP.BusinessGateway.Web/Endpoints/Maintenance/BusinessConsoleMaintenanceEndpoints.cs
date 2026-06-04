using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Maintenance;

public abstract class AuthorizedBusinessMaintenanceProxyEndpoint<TRequest, TResponse>(
    IBusinessGatewayAuthorizationClient auth,
    string permissionCode)
    : AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(auth, permissionCode)
    where TRequest : notnull
{
    protected override JsonSerializerOptions? ResponseJsonOptions => EquipmentRuntimeJson.Options;
}

[Tags("Business Console Maintenance")]
[HttpPost("/api/business-console/v1/maintenance/work-orders")]
[BusinessGatewayOperationId("createBusinessConsoleMaintenanceWorkOrder")]
public sealed class CreateBusinessConsoleMaintenanceWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateMaintenanceWorkOrderRequest, BusinessConsoleCreateMaintenanceWorkOrderResponse>(
        auth,
        BusinessGatewayPermissions.MaintenanceWorkOrdersManage)
{
    protected override string OrganizationId(BusinessConsoleCreateMaintenanceWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateMaintenanceWorkOrderRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleCreateMaintenanceWorkOrderRequest request) => "maintenance-work-order";

    protected override string? ResourceId(BusinessConsoleCreateMaintenanceWorkOrderRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleCreateMaintenanceWorkOrderResponse> ForwardAsync(
        BusinessConsoleCreateMaintenanceWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        maintenance.CreateWorkOrderAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Maintenance")]
[HttpPost("/api/business-console/v1/maintenance/work-orders/{workOrderId}/complete")]
[BusinessGatewayOperationId("completeBusinessConsoleMaintenanceWorkOrder")]
public sealed class CompleteBusinessConsoleMaintenanceWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCompleteMaintenanceWorkOrderRequest, BusinessConsoleCompleteMaintenanceWorkOrderResponse>(
        auth,
        BusinessGatewayPermissions.MaintenanceWorkOrdersManage)
{
    protected override string OrganizationId(BusinessConsoleCompleteMaintenanceWorkOrderRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCompleteMaintenanceWorkOrderRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleCompleteMaintenanceWorkOrderRequest request) => "maintenance-work-order";

    protected override string? ResourceId(BusinessConsoleCompleteMaintenanceWorkOrderRequest request) => Route<string>("workOrderId");

    protected override Task<BusinessConsoleCompleteMaintenanceWorkOrderResponse> ForwardAsync(
        BusinessConsoleCompleteMaintenanceWorkOrderRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        maintenance.CompleteWorkOrderAsync(tokenProvider.BearerToken, Route<string>("workOrderId")!, request, cancellationToken);
}

[Tags("Business Console Maintenance")]
[HttpGet("/api/business-console/v1/maintenance/work-orders")]
[BusinessGatewayOperationId("listBusinessConsoleMaintenanceWorkOrders")]
public sealed class ListBusinessConsoleMaintenanceWorkOrdersEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMaintenanceContextRequest, BusinessConsoleMaintenanceWorkOrderListResponse>(
        auth,
        BusinessGatewayPermissions.MaintenanceWorkOrdersRead)
{
    protected override string OrganizationId(BusinessConsoleMaintenanceContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMaintenanceContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMaintenanceWorkOrderListResponse> ForwardAsync(
        BusinessConsoleMaintenanceContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        maintenance.ListWorkOrdersAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Maintenance")]
[HttpGet("/api/business-console/v1/maintenance/work-orders/{workOrderId}")]
[BusinessGatewayOperationId("getBusinessConsoleMaintenanceWorkOrder")]
public sealed class GetBusinessConsoleMaintenanceWorkOrderEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMaintenanceContextRequest, BusinessConsoleMaintenanceWorkOrderItem>(
        auth,
        BusinessGatewayPermissions.MaintenanceWorkOrdersRead)
{
    protected override string OrganizationId(BusinessConsoleMaintenanceContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMaintenanceContextRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleMaintenanceContextRequest request) => "maintenance-work-order";

    protected override string? ResourceId(BusinessConsoleMaintenanceContextRequest request) => Route<string>("workOrderId");

    protected override Task<BusinessConsoleMaintenanceWorkOrderItem> ForwardAsync(
        BusinessConsoleMaintenanceContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        maintenance.GetWorkOrderAsync(tokenProvider.BearerToken, Route<string>("workOrderId")!, request, cancellationToken);
}

[Tags("Business Console Maintenance")]
[HttpPost("/api/business-console/v1/maintenance/plans")]
[BusinessGatewayOperationId("createBusinessConsoleMaintenancePlan")]
public sealed class CreateBusinessConsoleMaintenancePlanEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateMaintenancePlanRequest, BusinessConsoleCreateMaintenancePlanResponse>(
        auth,
        BusinessGatewayPermissions.MaintenancePlansManage)
{
    protected override string OrganizationId(BusinessConsoleCreateMaintenancePlanRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateMaintenancePlanRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleCreateMaintenancePlanRequest request) => "maintenance-plan";

    protected override string? ResourceId(BusinessConsoleCreateMaintenancePlanRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleCreateMaintenancePlanResponse> ForwardAsync(
        BusinessConsoleCreateMaintenancePlanRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        maintenance.CreatePlanAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Maintenance")]
[HttpGet("/api/business-console/v1/maintenance/plans")]
[BusinessGatewayOperationId("listBusinessConsoleMaintenancePlans")]
public sealed class ListBusinessConsoleMaintenancePlansEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleMaintenanceContextRequest, BusinessConsoleMaintenancePlanListResponse>(
        auth,
        BusinessGatewayPermissions.MaintenancePlansRead)
{
    protected override string OrganizationId(BusinessConsoleMaintenanceContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleMaintenanceContextRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleMaintenancePlanListResponse> ForwardAsync(
        BusinessConsoleMaintenanceContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        maintenance.ListPlansAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Maintenance")]
[HttpPost("/api/business-console/v1/maintenance/inspections")]
[BusinessGatewayOperationId("recordBusinessConsoleMaintenanceInspection")]
public sealed class RecordBusinessConsoleMaintenanceInspectionEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleRecordMaintenanceInspectionRequest, BusinessConsoleRecordMaintenanceInspectionResponse>(
        auth,
        BusinessGatewayPermissions.MaintenancePlansManage)
{
    protected override string OrganizationId(BusinessConsoleRecordMaintenanceInspectionRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleRecordMaintenanceInspectionRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleRecordMaintenanceInspectionRequest request) => "maintenance-inspection";

    protected override string? ResourceId(BusinessConsoleRecordMaintenanceInspectionRequest request) => request.WorkOrderId ?? request.PlanId;

    protected override Task<BusinessConsoleRecordMaintenanceInspectionResponse> ForwardAsync(
        BusinessConsoleRecordMaintenanceInspectionRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        maintenance.RecordInspectionAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console Maintenance")]
[HttpGet("/api/business-console/v1/maintenance/availability-windows")]
[BusinessGatewayOperationId("queryBusinessConsoleMaintenanceAvailabilityWindows")]
public sealed class QueryBusinessConsoleMaintenanceAvailabilityWindowsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessMaintenanceProxyEndpoint<BusinessConsoleEquipmentAvailabilityRequest, EquipmentRuntimeAvailabilityResponse>(
        auth,
        BusinessGatewayPermissions.MaintenanceWorkOrdersRead)
{
    protected override string OrganizationId(BusinessConsoleEquipmentAvailabilityRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleEquipmentAvailabilityRequest request) => request.EnvironmentId;

    protected override Task<EquipmentRuntimeAvailabilityResponse> ForwardAsync(
        BusinessConsoleEquipmentAvailabilityRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        maintenance.GetAvailabilityWindowsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleMaintenanceContextRequestValidator : Validator<BusinessConsoleMaintenanceContextRequest>
{
    public BusinessConsoleMaintenanceContextRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleCreateMaintenanceWorkOrderRequestValidator : Validator<BusinessConsoleCreateMaintenanceWorkOrderRequest>
{
    public BusinessConsoleCreateMaintenanceWorkOrderRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Priority).NotEmpty().MaximumLength(40);
        RuleFor(x => x.SourceAlarmId).MaximumLength(100);
        RuleFor(x => x.OpenedBy).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AssetUnavailableReason).MaximumLength(200);
    }
}

public sealed class BusinessConsoleCompleteMaintenanceWorkOrderRequestValidator : Validator<BusinessConsoleCompleteMaintenanceWorkOrderRequest>
{
    public BusinessConsoleCompleteMaintenanceWorkOrderRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Result).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DowntimeReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DowntimeMinutes).GreaterThanOrEqualTo(0);
        RuleForEach(x => x.SpareParts).ChildRules(line =>
        {
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UomCode).MaximumLength(30);
        });
    }
}

public sealed class BusinessConsoleCreateMaintenancePlanRequestValidator : Validator<BusinessConsoleCreateMaintenancePlanRequest>
{
    public BusinessConsoleCreateMaintenancePlanRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Interval).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Owner).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WindowEndUtc)
            .GreaterThan(x => x.WindowStartUtc)
            .When(x => x.WindowStartUtc.HasValue && x.WindowEndUtc.HasValue);
    }
}

public sealed class BusinessConsoleRecordMaintenanceInspectionRequestValidator : Validator<BusinessConsoleRecordMaintenanceInspectionRequest>
{
    public BusinessConsoleRecordMaintenanceInspectionRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PlanId).MaximumLength(100);
        RuleFor(x => x.WorkOrderId).MaximumLength(100);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.PlanId) || !string.IsNullOrWhiteSpace(x.WorkOrderId));
        RuleFor(x => x.Inspector).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Result).NotEmpty().MaximumLength(100);
    }
}
