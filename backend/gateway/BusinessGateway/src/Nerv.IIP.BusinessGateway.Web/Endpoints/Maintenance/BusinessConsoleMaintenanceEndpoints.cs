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
