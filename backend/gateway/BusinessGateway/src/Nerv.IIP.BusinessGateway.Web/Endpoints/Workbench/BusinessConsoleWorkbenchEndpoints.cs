using System.Security.Claims;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Workbench;

[Tags("Business Console Workbench")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
[HttpGet("/api/business-console/v1/workbench/summary")]
[BusinessGatewayOperationId("getBusinessConsoleWorkbenchSummary")]
public sealed class GetBusinessConsoleWorkbenchSummaryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessApprovalClient approval,
    IBusinessNotificationClient notification,
    IBusinessQualityClient quality,
    IBusinessIndustrialTelemetryClient industrialTelemetry,
    IBusinessMesClient mes,
    IInternalServiceTokenProvider tokenProvider)
    : Endpoint<BusinessConsoleWorkbenchSummaryRequest, ResponseData<BusinessConsoleWorkbenchSummaryResponse>>
{
    private const int DefaultTake = 20;
    private const int MaxTake = 100;

    public override async Task HandleAsync(BusinessConsoleWorkbenchSummaryRequest req, CancellationToken ct)
    {
        var bearerToken = await RequireBearerAndScopeAsync(req, ct);
        if (bearerToken is null)
        {
            return;
        }

        var take = ClampTake(req.Take);
        var sourceStatuses = new Dictionary<string, BusinessConsoleWorkbenchSourceStatus>(StringComparer.Ordinal);
        var kpis = new List<BusinessConsoleWorkbenchKpiItem>();
        var todos = new List<BusinessConsoleWorkbenchTodoItem>();
        var messages = new List<BusinessConsoleWorkbenchMessageItem>();
        var alerts = new List<BusinessConsoleWorkbenchAlertItem>();

        sourceStatuses["BusinessInventory"] = new(
            "BusinessInventory",
            "unsupported",
            BusinessGatewayPermissions.InventoryLedgerRead,
            "global-inventory-workbench-summary-not-connected");

        await AddApprovalAsync(req, bearerToken, take, sourceStatuses, todos, ct);
        await AddNotificationAsync(req, bearerToken, take, sourceStatuses, messages, todos, ct);
        await AddQualityAsync(req, bearerToken, MaxTake, sourceStatuses, kpis, ct);
        await AddIndustrialTelemetryAsync(req, bearerToken, sourceStatuses, alerts, ct);
        await AddMesAsync(req, bearerToken, MaxTake, sourceStatuses, kpis, ct);

        var response = new BusinessConsoleWorkbenchSummaryResponse(
            req.OrganizationId,
            req.EnvironmentId,
            kpis.OrderBy(x => x.Source, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            new BusinessConsoleWorkbenchTodoSummary(SummaryStatus(todos.Count, sourceStatuses, "BusinessApproval", "Notification"), todos.Count, todos.Take(take).ToArray()),
            new BusinessConsoleWorkbenchMessageSummary(SummaryStatus(messages.Count, sourceStatuses, "Notification"), messages.Count, messages.Count(x => x.Status == "unread"), messages.Take(take).ToArray()),
            new BusinessConsoleWorkbenchAlertSummary(SummaryStatus(alerts.Count, sourceStatuses, "IndustrialTelemetry"), alerts.Count, alerts.Count(x => x.Severity == "critical"), alerts.Take(take).ToArray()),
            sourceStatuses.Values.OrderBy(x => x.Source, StringComparer.Ordinal).ToArray());
        await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status200OK, response, ct);
    }

    private async Task AddApprovalAsync(
        BusinessConsoleWorkbenchSummaryRequest request,
        string bearerToken,
        int take,
        Dictionary<string, BusinessConsoleWorkbenchSourceStatus> sourceStatuses,
        List<BusinessConsoleWorkbenchTodoItem> todos,
        CancellationToken cancellationToken)
    {
        var authorization = await CheckSourceAsync(
            bearerToken,
            BusinessGatewayPermissions.ApprovalsRead,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            sourceStatuses["BusinessApproval"] = SourceStatus.Forbidden("BusinessApproval", BusinessGatewayPermissions.ApprovalsRead);
            return;
        }

        try
        {
            var principalId = authorization.PrincipalId ?? authorization.LoginName ?? string.Empty;
            var principalType = authorization.PrincipalType ?? "user";
            var response = await approval.ListPendingTasksAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleApprovalTaskListRequest(request.OrganizationId, request.EnvironmentId, principalType, principalId),
                cancellationToken);
            todos.AddRange(response.Items
                .Take(take)
                .Select(item => new BusinessConsoleWorkbenchTodoItem(
                    "BusinessApproval",
                    item.ChainId,
                    item.DocumentType,
                    "pending",
                    item.DocumentId,
                    item.DueAtUtc)));
            sourceStatuses["BusinessApproval"] = SourceStatus.Available("BusinessApproval", BusinessGatewayPermissions.ApprovalsRead);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["BusinessApproval"] = SourceStatus.Unavailable("BusinessApproval", BusinessGatewayPermissions.ApprovalsRead);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["BusinessApproval"] = SourceStatus.Unavailable("BusinessApproval", BusinessGatewayPermissions.ApprovalsRead);
        }
    }

    private async Task AddNotificationAsync(
        BusinessConsoleWorkbenchSummaryRequest request,
        string bearerToken,
        int take,
        Dictionary<string, BusinessConsoleWorkbenchSourceStatus> sourceStatuses,
        List<BusinessConsoleWorkbenchMessageItem> messages,
        List<BusinessConsoleWorkbenchTodoItem> todos,
        CancellationToken cancellationToken)
    {
        var messageAuthorization = await CheckSourceAsync(
            bearerToken,
            BusinessGatewayPermissions.NotificationMessagesRead,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        var taskAuthorization = await CheckSourceAsync(
            bearerToken,
            BusinessGatewayPermissions.NotificationTasksRead,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        if (!messageAuthorization.IsAllowed && !taskAuthorization.IsAllowed)
        {
            sourceStatuses["Notification"] = SourceStatus.Forbidden("Notification", BusinessGatewayPermissions.NotificationMessagesRead);
            return;
        }

        var notificationPermissionCode = messageAuthorization.IsAllowed
            ? BusinessGatewayPermissions.NotificationMessagesRead
            : BusinessGatewayPermissions.NotificationTasksRead;
        var principalRef = PrincipalReference(messageAuthorization) ?? PrincipalReference(taskAuthorization);
        if (principalRef is null)
        {
            sourceStatuses["Notification"] = SourceStatus.Unavailable("Notification", notificationPermissionCode, "principal-unresolved");
            return;
        }

        try
        {
            if (messageAuthorization.IsAllowed)
            {
                var response = await notification.ListMessagesAsync(
                    tokenProvider.BearerToken,
                    new BusinessConsoleNotificationListRequest(request.OrganizationId, request.EnvironmentId, principalRef, "unread", take),
                    cancellationToken);
                messages.AddRange(response.Items.Select(item => new BusinessConsoleWorkbenchMessageItem(
                    item.MessageId,
                    item.Status,
                    item.Severity,
                    item.Resource?.ResourceType,
                    item.Resource?.ResourceId,
                    item.CreatedAtUtc)));
            }

            if (taskAuthorization.IsAllowed)
            {
                var response = await notification.ListTasksAsync(
                    tokenProvider.BearerToken,
                    new BusinessConsoleNotificationListRequest(request.OrganizationId, request.EnvironmentId, principalRef, "open", take),
                    cancellationToken);
                todos.AddRange(response.Items.Select(item => new BusinessConsoleWorkbenchTodoItem(
                    "Notification",
                    item.TaskId,
                    item.TaskType,
                    item.Status,
                    item.ActionRef,
                    null)));
            }

            sourceStatuses["Notification"] = SourceStatus.Available(
                "Notification",
                notificationPermissionCode);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["Notification"] = SourceStatus.Unavailable("Notification", notificationPermissionCode);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["Notification"] = SourceStatus.Unavailable("Notification", notificationPermissionCode);
        }
    }

    private async Task AddQualityAsync(
        BusinessConsoleWorkbenchSummaryRequest request,
        string bearerToken,
        int take,
        Dictionary<string, BusinessConsoleWorkbenchSourceStatus> sourceStatuses,
        List<BusinessConsoleWorkbenchKpiItem> kpis,
        CancellationToken cancellationToken)
    {
        var authorization = await CheckSourceAsync(
            bearerToken,
            BusinessGatewayPermissions.QualityNcrRead,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            sourceStatuses["BusinessQuality"] = SourceStatus.Forbidden("BusinessQuality", BusinessGatewayPermissions.QualityNcrRead);
            return;
        }

        try
        {
            var response = await quality.ListNcrsAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleQualityListRequest(request.OrganizationId, request.EnvironmentId, "open", take),
                cancellationToken);
            kpis.Add(new("openNcrs", "Open NCRs", response.Items.Count, "BusinessQuality", "available"));
            sourceStatuses["BusinessQuality"] = SourceStatus.Available("BusinessQuality", BusinessGatewayPermissions.QualityNcrRead);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["BusinessQuality"] = SourceStatus.Unavailable("BusinessQuality", BusinessGatewayPermissions.QualityNcrRead);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["BusinessQuality"] = SourceStatus.Unavailable("BusinessQuality", BusinessGatewayPermissions.QualityNcrRead);
        }
    }

    private async Task AddIndustrialTelemetryAsync(
        BusinessConsoleWorkbenchSummaryRequest request,
        string bearerToken,
        Dictionary<string, BusinessConsoleWorkbenchSourceStatus> sourceStatuses,
        List<BusinessConsoleWorkbenchAlertItem> alerts,
        CancellationToken cancellationToken)
    {
        var authorization = await CheckSourceAsync(
            bearerToken,
            BusinessGatewayPermissions.IiotAlarmsRead,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            sourceStatuses["IndustrialTelemetry"] = SourceStatus.Forbidden("IndustrialTelemetry", BusinessGatewayPermissions.IiotAlarmsRead);
            return;
        }

        try
        {
            var response = await industrialTelemetry.ListActiveAlarmsAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleEquipmentContextRequest(request.OrganizationId, request.EnvironmentId),
                cancellationToken);
            alerts.AddRange(response.Items.Select(item => new BusinessConsoleWorkbenchAlertItem(
                item.AlarmEventId,
                item.DeviceAssetId,
                item.AlarmCode,
                item.Severity,
                item.RaisedAtUtc)));
            sourceStatuses["IndustrialTelemetry"] = SourceStatus.Available("IndustrialTelemetry", BusinessGatewayPermissions.IiotAlarmsRead);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["IndustrialTelemetry"] = SourceStatus.Unavailable("IndustrialTelemetry", BusinessGatewayPermissions.IiotAlarmsRead);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["IndustrialTelemetry"] = SourceStatus.Unavailable("IndustrialTelemetry", BusinessGatewayPermissions.IiotAlarmsRead);
        }
    }

    private async Task AddMesAsync(
        BusinessConsoleWorkbenchSummaryRequest request,
        string bearerToken,
        int take,
        Dictionary<string, BusinessConsoleWorkbenchSourceStatus> sourceStatuses,
        List<BusinessConsoleWorkbenchKpiItem> kpis,
        CancellationToken cancellationToken)
    {
        var workOrders = await CheckSourceAsync(
            bearerToken,
            BusinessGatewayPermissions.MesWorkOrdersRead,
            request.OrganizationId,
            request.EnvironmentId,
            cancellationToken);
        if (!workOrders.IsAllowed)
        {
            sourceStatuses["BusinessMES"] = SourceStatus.Forbidden("BusinessMES", BusinessGatewayPermissions.MesWorkOrdersRead);
            return;
        }

        try
        {
            var response = await mes.ListWorkOrdersAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleMesListRequest(request.OrganizationId, request.EnvironmentId, "released", take),
                cancellationToken);
            kpis.Add(new("releasedWorkOrders", "Released work orders", response.Items.Count, "BusinessMES", "available"));
            sourceStatuses["BusinessMES"] = SourceStatus.Available("BusinessMES", BusinessGatewayPermissions.MesWorkOrdersRead);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["BusinessMES"] = SourceStatus.Unavailable("BusinessMES", BusinessGatewayPermissions.MesWorkOrdersRead);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["BusinessMES"] = SourceStatus.Unavailable("BusinessMES", BusinessGatewayPermissions.MesWorkOrdersRead);
        }
    }

    private Task<BusinessGatewayAuthorizationResult> CheckSourceAsync(
        string bearerToken,
        string permissionCode,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken) =>
        auth.CheckAsync(
            bearerToken,
            new BusinessGatewayPermissionRequirement(permissionCode, organizationId, environmentId, null, null),
            cancellationToken);

    private async Task<string?> RequireBearerAndScopeAsync(BusinessConsoleWorkbenchSummaryRequest request, CancellationToken cancellationToken)
    {
        var bearerToken = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized.", cancellationToken);
            return null;
        }

        if (!string.Equals(HttpContext.User.FindFirstValue("organizationId"), request.OrganizationId, StringComparison.Ordinal) ||
            !string.Equals(HttpContext.User.FindFirstValue("environmentId"), request.EnvironmentId, StringComparison.Ordinal))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status403Forbidden, "Forbidden.", cancellationToken);
            return null;
        }

        return bearerToken;
    }

    private static int ClampTake(int take) => take switch
    {
        <= 0 => DefaultTake,
        > MaxTake => MaxTake,
        _ => take,
    };

    private static string? PrincipalReference(BusinessGatewayAuthorizationResult authorization)
    {
        if (!string.IsNullOrWhiteSpace(authorization.PrincipalId))
        {
            return authorization.PrincipalId;
        }

        return string.IsNullOrWhiteSpace(authorization.LoginName) ? null : authorization.LoginName;
    }

    private static string SummaryStatus(
        int count,
        IReadOnlyDictionary<string, BusinessConsoleWorkbenchSourceStatus> sourceStatuses,
        params string[] sources)
    {
        if (count > 0)
        {
            return "available";
        }

        return sources.Any(source => sourceStatuses.TryGetValue(source, out var status) && status.Status == "available")
            ? "available"
            : "unavailable";
    }

    private static class SourceStatus
    {
        public static BusinessConsoleWorkbenchSourceStatus Available(string source, string permissionCode) =>
            new(source, "available", permissionCode, null);

        public static BusinessConsoleWorkbenchSourceStatus Forbidden(string source, string permissionCode) =>
            new(source, "forbidden", permissionCode, "permission-denied");

        public static BusinessConsoleWorkbenchSourceStatus Unavailable(string source, string permissionCode, string reason = "source-unavailable") =>
            new(source, "unavailable", permissionCode, reason);
    }
}

public sealed class BusinessConsoleWorkbenchSummaryRequestValidator : Validator<BusinessConsoleWorkbenchSummaryRequest>
{
    public BusinessConsoleWorkbenchSummaryRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Take).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100);
    }
}
