using FastEndpoints;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;
using System.Text.Json;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Equipment;

public abstract class AuthorizedBusinessEquipmentProxyEndpoint<TRequest, TResponse>(
    IBusinessGatewayAuthorizationClient auth,
    string permissionCode)
    : AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(auth, permissionCode)
    where TRequest : notnull
{
    protected override JsonSerializerOptions? ResponseJsonOptions => EquipmentRuntimeJson.Options;
}

[Tags("Business Console Equipment")]
[HttpGet("/api/business-console/v1/equipment/overview")]
[BusinessGatewayOperationId("getBusinessConsoleEquipmentOverview")]
public sealed class GetBusinessConsoleEquipmentOverviewEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient industrialTelemetry,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessEquipmentProxyEndpoint<BusinessConsoleEquipmentOverviewRequest, BusinessConsoleEquipmentOverviewResponse>(
        auth,
        BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleEquipmentOverviewRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleEquipmentOverviewRequest request) => request.EnvironmentId;

    protected override async Task<BusinessConsoleEquipmentOverviewResponse> ForwardAsync(
        BusinessConsoleEquipmentOverviewRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var window = DefaultWindow();
        var deviceAssetIds = BusinessConsoleEquipmentDeviceScope.NormalizeDeviceAssetIds(request.DeviceAssetIds);
        var normalizedDeviceAssetIds = string.Join(',', deviceAssetIds);
        var availabilityRequest = new BusinessConsoleEquipmentAvailabilityRequest(
            request.OrganizationId,
            request.EnvironmentId,
            window.StartUtc,
            window.EndUtc,
            normalizedDeviceAssetIds,
            null);
        var availability = await QueryCombinedAvailabilityAsync(
            tokenProvider.BearerToken,
            industrialTelemetry,
            maintenance,
            availabilityRequest,
            cancellationToken);
        var activeBlocks = availability.Items
            .Where(item => item.AvailabilityStatus != EquipmentRuntimeAvailabilityStatus.Available)
            .ToArray();
        var blocksByDevice = activeBlocks
            .GroupBy(item => item.DeviceAssetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var currentStates = new List<EquipmentRuntimeCurrentStateResponse>(deviceAssetIds.Length);
        foreach (var deviceAssetId in deviceAssetIds)
        {
            currentStates.Add(await industrialTelemetry.GetDeviceCurrentStateAsync(
                tokenProvider.BearerToken,
                deviceAssetId,
                new BusinessConsoleEquipmentContextRequest(request.OrganizationId, request.EnvironmentId),
                cancellationToken));
        }

        var devices = currentStates
            .Select(currentState => new BusinessConsoleEquipmentDeviceSummary(
                currentState.DeviceAssetId,
                currentState.CurrentState,
                currentState.IsSourceFresh,
                currentState.ActiveAlarms.Count,
                blocksByDevice.GetValueOrDefault(currentState.DeviceAssetId)))
            .OrderBy(device => device.DeviceAssetId, StringComparer.Ordinal)
            .ToArray();
        return new BusinessConsoleEquipmentOverviewResponse(devices, activeBlocks);
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) DefaultWindow()
    {
        var startUtc = DateTimeOffset.UtcNow;
        return (startUtc, startUtc.AddHours(8));
    }

    internal static async Task<EquipmentRuntimeAvailabilityResponse> QueryCombinedAvailabilityAsync(
        string internalBearerToken,
        IBusinessIndustrialTelemetryClient industrialTelemetry,
        IBusinessMaintenanceClient maintenance,
        BusinessConsoleEquipmentAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var iiot = await industrialTelemetry.GetRuntimeAvailabilityAsync(internalBearerToken, request, cancellationToken);
        var maintenanceWindows = await maintenance.GetAvailabilityWindowsAsync(internalBearerToken, request, cancellationToken);
        return new EquipmentRuntimeAvailabilityResponse(
            iiot.ContractVersion,
            request.OrganizationId,
            request.EnvironmentId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            BusinessConsoleEquipmentAvailabilityMerger.Merge(iiot.Items, maintenanceWindows.Items));
    }
}

[Tags("Business Console Equipment")]
[HttpGet("/api/business-console/v1/equipment/devices/{deviceAssetId}")]
[BusinessGatewayOperationId("getBusinessConsoleEquipmentDevice")]
public sealed class GetBusinessConsoleEquipmentDeviceEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient industrialTelemetry,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessEquipmentProxyEndpoint<BusinessConsoleEquipmentContextRequest, BusinessConsoleEquipmentDeviceDetailResponse>(
        auth,
        BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleEquipmentContextRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleEquipmentContextRequest request) => request.EnvironmentId;

    protected override string ResourceType(BusinessConsoleEquipmentContextRequest request) => "device-asset";

    protected override string? ResourceId(BusinessConsoleEquipmentContextRequest request) => Route<string>("deviceAssetId");

    protected override async Task<BusinessConsoleEquipmentDeviceDetailResponse> ForwardAsync(
        BusinessConsoleEquipmentContextRequest request,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        var deviceAssetId = Route<string>("deviceAssetId")!;
        var currentState = await industrialTelemetry.GetDeviceCurrentStateAsync(
            tokenProvider.BearerToken,
            deviceAssetId,
            request,
            cancellationToken);
        var windowStartUtc = DateTimeOffset.UtcNow;
        var availabilityRequest = new BusinessConsoleEquipmentAvailabilityRequest(
            request.OrganizationId,
            request.EnvironmentId,
            windowStartUtc,
            windowStartUtc.AddHours(8),
            deviceAssetId,
            null);
        var iiot = await industrialTelemetry.GetDeviceRuntimeAvailabilityAsync(
            tokenProvider.BearerToken,
            deviceAssetId,
            availabilityRequest,
            cancellationToken);
        var maintenanceWindows = await maintenance.GetAssetAvailabilityWindowsAsync(
            tokenProvider.BearerToken,
            deviceAssetId,
            availabilityRequest,
            cancellationToken);
        var availability = new EquipmentRuntimeAvailabilityResponse(
            iiot.ContractVersion,
            availabilityRequest.OrganizationId,
            availabilityRequest.EnvironmentId,
            availabilityRequest.WindowStartUtc,
            availabilityRequest.WindowEndUtc,
            BusinessConsoleEquipmentAvailabilityMerger.Merge(iiot.Items, maintenanceWindows.Items));
        return new BusinessConsoleEquipmentDeviceDetailResponse(currentState, availability);
    }
}

[Tags("Business Console Equipment")]
[HttpGet("/api/business-console/v1/equipment/availability")]
[BusinessGatewayOperationId("getBusinessConsoleEquipmentAvailability")]
public sealed class GetBusinessConsoleEquipmentAvailabilityEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient industrialTelemetry,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessEquipmentProxyEndpoint<BusinessConsoleEquipmentAvailabilityRequest, EquipmentRuntimeAvailabilityResponse>(
        auth,
        BusinessGatewayPermissions.IiotTelemetryRead)
{
    protected override string OrganizationId(BusinessConsoleEquipmentAvailabilityRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleEquipmentAvailabilityRequest request) => request.EnvironmentId;

    protected override Task<EquipmentRuntimeAvailabilityResponse> ForwardAsync(
        BusinessConsoleEquipmentAvailabilityRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        GetBusinessConsoleEquipmentOverviewEndpoint.QueryCombinedAvailabilityAsync(
            tokenProvider.BearerToken,
            industrialTelemetry,
            maintenance,
            request,
            cancellationToken);
}

[Tags("Business Console Equipment")]
[HttpGet("/api/business-console/v1/equipment/alarms")]
[BusinessGatewayOperationId("listBusinessConsoleEquipmentAlarms")]
public sealed class ListBusinessConsoleEquipmentAlarmsEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessIndustrialTelemetryClient industrialTelemetry,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessEquipmentProxyEndpoint<BusinessConsoleEquipmentAlarmListRequest, BusinessConsoleEquipmentAlarmListPageResponse>(
        auth,
        BusinessGatewayPermissions.IiotAlarmsRead)
{
    protected override string OrganizationId(BusinessConsoleEquipmentAlarmListRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleEquipmentAlarmListRequest request) => request.EnvironmentId;

    protected override string? ResourceType(BusinessConsoleEquipmentAlarmListRequest request) => request.DeviceAssetId is null ? null : "device-asset";

    protected override string? ResourceId(BusinessConsoleEquipmentAlarmListRequest request) => request.DeviceAssetId;

    protected override Task<BusinessConsoleEquipmentAlarmListPageResponse> ForwardAsync(
        BusinessConsoleEquipmentAlarmListRequest request,
        string bearerToken,
        CancellationToken cancellationToken) =>
        industrialTelemetry.ListActiveAlarmsAsync(tokenProvider.BearerToken, request, cancellationToken);
}

public sealed class BusinessConsoleEquipmentAlarmListRequestValidator : Validator<BusinessConsoleEquipmentAlarmListRequest>
{
    public BusinessConsoleEquipmentAlarmListRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).MaximumLength(150);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class BusinessConsoleEquipmentContextRequestValidator : Validator<BusinessConsoleEquipmentContextRequest>
{
    public BusinessConsoleEquipmentContextRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class BusinessConsoleEquipmentOverviewRequestValidator : Validator<BusinessConsoleEquipmentOverviewRequest>
{
    public BusinessConsoleEquipmentOverviewRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetIds)
            .NotEmpty()
            .MaximumLength(1000)
            .Must(value => BusinessConsoleEquipmentDeviceScope.NormalizeDeviceAssetIds(value).Length > 0)
            .WithMessage("deviceAssetIds must contain at least one device asset id.")
            .Must(value => BusinessConsoleEquipmentDeviceScope.NormalizeDeviceAssetIds(value).Length <= BusinessConsoleEquipmentDeviceScope.MaxDeviceAssetIds)
            .WithMessage($"deviceAssetIds cannot contain more than {BusinessConsoleEquipmentDeviceScope.MaxDeviceAssetIds} device asset ids.");
    }
}

public sealed class BusinessConsoleEquipmentAvailabilityRequestValidator : Validator<BusinessConsoleEquipmentAvailabilityRequest>
{
    public BusinessConsoleEquipmentAvailabilityRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
    }
}

internal static class BusinessConsoleEquipmentDeviceScope
{
    internal const int MaxDeviceAssetIds = 50;

    internal static string[] NormalizeDeviceAssetIds(string? deviceAssetIds) =>
        (deviceAssetIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(deviceAssetId => !string.IsNullOrWhiteSpace(deviceAssetId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

internal static class BusinessConsoleEquipmentAvailabilityMerger
{
    internal static IReadOnlyCollection<EquipmentRuntimeAvailabilityWindowContract> Merge(
        params IReadOnlyCollection<EquipmentRuntimeAvailabilityWindowContract>[] sources) =>
        sources
            .SelectMany(source => source)
            .GroupBy(window => new AvailabilityWindowKey(
                window.DeviceAssetId,
                window.SourceType,
                window.SourceReferenceId,
                window.StartUtc,
                window.EndUtc,
                window.ReasonCode))
            .Select(group => group
                .OrderBy(window => window.Severity)
                .ThenBy(window => window.AvailabilityStatus)
                .ThenBy(window => window.WorkCenterId, StringComparer.Ordinal)
                .ThenBy(window => window.MessageKey, StringComparer.Ordinal)
                .First())
            .OrderBy(window => window.DeviceAssetId, StringComparer.Ordinal)
            .ThenBy(window => window.StartUtc)
            .ThenBy(window => window.EndUtc)
            .ThenBy(window => window.Severity)
            .ThenBy(window => window.SourceType)
            .ThenBy(window => window.ReasonCode, StringComparer.Ordinal)
            .ThenBy(window => window.SourceReferenceId, StringComparer.Ordinal)
            .ToArray();

    private readonly record struct AvailabilityWindowKey(
        string DeviceAssetId,
        EquipmentRuntimeSourceType SourceType,
        string SourceReferenceId,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        string ReasonCode);
}
