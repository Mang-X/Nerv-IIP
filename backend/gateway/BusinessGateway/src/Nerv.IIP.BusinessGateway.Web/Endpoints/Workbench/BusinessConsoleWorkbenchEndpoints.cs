using System.Security.Claims;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.BusinessGateway.Web.Application.Resilience;
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
    IInternalServiceTokenProvider tokenProvider,
    BusinessGatewayDownstreamHealthState healthState)
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
        await AddIndustrialTelemetryAsync(req, bearerToken, take, sourceStatuses, alerts, ct);
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
        var authorization = await TryCheckSourceAsync(
            "BusinessApproval",
            bearerToken,
            BusinessGatewayPermissions.ApprovalsRead,
            request.OrganizationId,
            request.EnvironmentId,
            sourceStatuses,
            cancellationToken);
        if (authorization is null)
        {
            return;
        }

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
            sourceStatuses["BusinessApproval"] = SourceAvailable("BusinessApproval", BusinessGatewayPermissions.ApprovalsRead);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["BusinessApproval"] = SourceUnavailable("BusinessApproval", BusinessGatewayPermissions.ApprovalsRead);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["BusinessApproval"] = SourceUnavailable("BusinessApproval", BusinessGatewayPermissions.ApprovalsRead);
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
        var messageAuthorization = await TryCheckSourceAsync(
            "Notification",
            bearerToken,
            BusinessGatewayPermissions.NotificationMessagesRead,
            request.OrganizationId,
            request.EnvironmentId,
            sourceStatuses,
            cancellationToken);
        var taskAuthorization = await TryCheckSourceAsync(
            "Notification",
            bearerToken,
            BusinessGatewayPermissions.NotificationTasksRead,
            request.OrganizationId,
            request.EnvironmentId,
            sourceStatuses,
            cancellationToken);
        if (messageAuthorization is null && taskAuthorization is null)
        {
            return;
        }

        var messagesAllowed = messageAuthorization?.IsAllowed == true;
        var tasksAllowed = taskAuthorization?.IsAllowed == true;
        if (!messagesAllowed && !tasksAllowed)
        {
            if (messageAuthorization is not null && taskAuthorization is not null)
            {
                sourceStatuses["Notification"] = SourceStatus.Forbidden("Notification", BusinessGatewayPermissions.NotificationMessagesRead);
            }

            return;
        }

        var notificationPermissionCode = messagesAllowed
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
            if (messagesAllowed)
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

            if (tasksAllowed)
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

            sourceStatuses["Notification"] = SourceAvailable(
                "Notification",
                notificationPermissionCode);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["Notification"] = SourceUnavailable("Notification", notificationPermissionCode);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["Notification"] = SourceUnavailable("Notification", notificationPermissionCode);
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
        var authorization = await TryCheckSourceAsync(
            "BusinessQuality",
            bearerToken,
            BusinessGatewayPermissions.QualityNcrRead,
            request.OrganizationId,
            request.EnvironmentId,
            sourceStatuses,
            cancellationToken);
        if (authorization is null)
        {
            return;
        }

        if (!authorization.IsAllowed)
        {
            sourceStatuses["BusinessQuality"] = SourceStatus.Forbidden("BusinessQuality", BusinessGatewayPermissions.QualityNcrRead);
            return;
        }

        try
        {
            var response = await quality.ListNcrsAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleQualityListRequest(request.OrganizationId, request.EnvironmentId, "open", Take: take),
                cancellationToken);
            kpis.Add(new("openNcrs", "Open NCRs", response.Total, "BusinessQuality", "available"));
            sourceStatuses["BusinessQuality"] = SourceAvailable("BusinessQuality", BusinessGatewayPermissions.QualityNcrRead);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["BusinessQuality"] = SourceUnavailable("BusinessQuality", BusinessGatewayPermissions.QualityNcrRead);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["BusinessQuality"] = SourceUnavailable("BusinessQuality", BusinessGatewayPermissions.QualityNcrRead);
        }
    }

    private async Task AddIndustrialTelemetryAsync(
        BusinessConsoleWorkbenchSummaryRequest request,
        string bearerToken,
        int take,
        Dictionary<string, BusinessConsoleWorkbenchSourceStatus> sourceStatuses,
        List<BusinessConsoleWorkbenchAlertItem> alerts,
        CancellationToken cancellationToken)
    {
        var authorization = await TryCheckSourceAsync(
            "IndustrialTelemetry",
            bearerToken,
            BusinessGatewayPermissions.IiotAlarmsRead,
            request.OrganizationId,
            request.EnvironmentId,
            sourceStatuses,
            cancellationToken);
        if (authorization is null)
        {
            return;
        }

        if (!authorization.IsAllowed)
        {
            sourceStatuses["IndustrialTelemetry"] = SourceStatus.Forbidden("IndustrialTelemetry", BusinessGatewayPermissions.IiotAlarmsRead);
            return;
        }

        try
        {
            var response = await industrialTelemetry.ListActiveAlarmsAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleEquipmentAlarmListRequest(request.OrganizationId, request.EnvironmentId, null, "raised", Take: take),
                cancellationToken);
            alerts.AddRange(response.Items.Select(item => new BusinessConsoleWorkbenchAlertItem(
                item.AlarmEventId,
                item.DeviceAssetId,
                item.AlarmCode,
                item.Severity,
                item.RaisedAtUtc)));
            sourceStatuses["IndustrialTelemetry"] = SourceAvailable("IndustrialTelemetry", BusinessGatewayPermissions.IiotAlarmsRead);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["IndustrialTelemetry"] = SourceUnavailable("IndustrialTelemetry", BusinessGatewayPermissions.IiotAlarmsRead);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["IndustrialTelemetry"] = SourceUnavailable("IndustrialTelemetry", BusinessGatewayPermissions.IiotAlarmsRead);
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
        var workOrders = await TryCheckSourceAsync(
            "BusinessMES",
            bearerToken,
            BusinessGatewayPermissions.MesWorkOrdersRead,
            request.OrganizationId,
            request.EnvironmentId,
            sourceStatuses,
            cancellationToken);
        if (workOrders is null)
        {
            return;
        }

        if (!workOrders.IsAllowed)
        {
            sourceStatuses["BusinessMES"] = SourceStatus.Forbidden("BusinessMES", BusinessGatewayPermissions.MesWorkOrdersRead);
            return;
        }

        try
        {
            var response = await mes.ListWorkOrdersAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleMesWorkOrderListRequest(request.OrganizationId, request.EnvironmentId, "released", Take: take),
                cancellationToken);
            kpis.Add(new("releasedWorkOrders", "Released work orders", response.Total, "BusinessMES", "available"));
            sourceStatuses["BusinessMES"] = SourceAvailable("BusinessMES", BusinessGatewayPermissions.MesWorkOrdersRead);
        }
        catch (BusinessServiceProxyException)
        {
            sourceStatuses["BusinessMES"] = SourceUnavailable("BusinessMES", BusinessGatewayPermissions.MesWorkOrdersRead);
        }
        catch (HttpRequestException)
        {
            sourceStatuses["BusinessMES"] = SourceUnavailable("BusinessMES", BusinessGatewayPermissions.MesWorkOrdersRead);
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

    private async Task<BusinessGatewayAuthorizationResult?> TryCheckSourceAsync(
        string source,
        string bearerToken,
        string permissionCode,
        string organizationId,
        string environmentId,
        Dictionary<string, BusinessConsoleWorkbenchSourceStatus> sourceStatuses,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CheckSourceAsync(
                bearerToken,
                permissionCode,
                organizationId,
                environmentId,
                cancellationToken);
        }
        catch (Exception ex) when (IsAuthorizationUnavailable(ex, cancellationToken))
        {
            healthState.RecordFailure("IAM", "iam-unavailable");
            sourceStatuses[source] = SourceStatus.Unavailable(source, permissionCode, "authorization-unavailable");
            return null;
        }
    }

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

    private static string? PrincipalReference(BusinessGatewayAuthorizationResult? authorization)
    {
        if (authorization is null)
        {
            return null;
        }

        var actorRef = !string.IsNullOrWhiteSpace(authorization.PrincipalId)
            ? authorization.PrincipalId
            : authorization.LoginName;
        if (string.IsNullOrWhiteSpace(actorRef))
        {
            return null;
        }

        var actorType = string.IsNullOrWhiteSpace(authorization.PrincipalType)
            ? "user"
            : authorization.PrincipalType;
        return BusinessGatewayPrincipalReferences.ToRecipientRef(actorType, actorRef);
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

    private static bool IsAuthorizationUnavailable(Exception ex, CancellationToken requestCancellationToken) =>
        ex is HttpRequestException
            || ex is TimeoutException
            || ex is TaskCanceledException && !requestCancellationToken.IsCancellationRequested;

    private BusinessConsoleWorkbenchSourceStatus SourceAvailable(string source, string permissionCode)
    {
        healthState.RecordSuccess(source);
        return SourceStatus.Available(source, permissionCode);
    }

    private BusinessConsoleWorkbenchSourceStatus SourceUnavailable(
        string source,
        string permissionCode,
        string reason = "source-unavailable")
    {
        healthState.RecordFailure(source, reason);
        return SourceStatus.Unavailable(source, permissionCode, reason);
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
