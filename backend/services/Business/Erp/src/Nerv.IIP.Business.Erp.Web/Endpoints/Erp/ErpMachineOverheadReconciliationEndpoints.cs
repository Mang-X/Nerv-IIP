using System.Security.Claims;
using System.Text.Json;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

public sealed record ReconcileWorkCenterMachineOverheadRequest(
    string WorkCenterId,
    string AccountingPeriodCode,
    decimal ActualFixedOverheadAmount,
    decimal ActualVariableOverheadAmount,
    string CurrencyCode,
    long AbnormalDowntimeTicks,
    AbnormalDowntimeDisposition AbnormalDowntimeDisposition,
    string SourceReference,
    string Reason);

public sealed record ReconcileWorkCenterMachineOverheadResponse(
    WorkCenterMachineOverheadReconciliationId WorkCenterMachineOverheadReconciliationId);

public sealed record ListWorkCenterMachineOverheadReconciliationsRequest(
    string AccountingPeriodCode,
    string? WorkCenterId = null,
    int PageNumber = 1,
    int PageSize = 50);

public sealed class ReconcileWorkCenterMachineOverheadEndpoint(
    ISender sender,
    IErpIntegrationEventContextAccessor eventContext,
    IErpMachineOverheadInternalScopeAuthorizer scopeAuthorizer,
    TimeProvider timeProvider)
    : ErpEndpoint<ReconcileWorkCenterMachineOverheadRequest, ResponseData<ReconcileWorkCenterMachineOverheadResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ReconcileWorkCenterMachineOverheadEndpoint>());

    public override async Task HandleAsync(ReconcileWorkCenterMachineOverheadRequest req, CancellationToken ct)
    {
        var authorization = scopeAuthorizer.ResolveAuthorizedScope(HttpContext);
        if (authorization is ErpInternalServiceScopeAuthorization.MissingRequiredHeader)
        {
            await ErpMachineOverheadEndpointResults.WriteMissingScopeHeadersAsync(HttpContext, ct);
            return;
        }
        if (authorization is ErpInternalServiceScopeAuthorization.Forbidden)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }
        var scope = ((ErpInternalServiceScopeAuthorization.Authorized)authorization).Scope;
        using var causationScope = eventContext.BeginScope(ErpCommandCausationIds.ForHttpCommand(
            "reconcile-work-center-machine-overhead",
            scope.OrganizationId,
            scope.EnvironmentId,
            req.WorkCenterId,
            req.AccountingPeriodCode,
            req.ActualFixedOverheadAmount,
            req.ActualVariableOverheadAmount,
            req.CurrencyCode,
            req.AbnormalDowntimeTicks,
            req.AbnormalDowntimeDisposition,
            req.SourceReference,
            req.Reason));
        var id = await sender.Send(new ReconcileWorkCenterMachineOverheadCommand(
            scope.OrganizationId,
            scope.EnvironmentId,
            req.WorkCenterId,
            req.AccountingPeriodCode,
            req.ActualFixedOverheadAmount,
            req.ActualVariableOverheadAmount,
            req.CurrencyCode,
            req.AbnormalDowntimeTicks,
            req.AbnormalDowntimeDisposition,
            scope.Actor,
            req.SourceReference,
            req.Reason,
            timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(new ReconcileWorkCenterMachineOverheadResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListWorkCenterMachineOverheadReconciliationsEndpoint(
    ISender sender,
    IErpMachineOverheadInternalScopeAuthorizer scopeAuthorizer)
    : ErpEndpoint<ListWorkCenterMachineOverheadReconciliationsRequest, ResponseData<ListWorkCenterMachineOverheadReconciliationsResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ListWorkCenterMachineOverheadReconciliationsEndpoint>());

    public override async Task HandleAsync(ListWorkCenterMachineOverheadReconciliationsRequest req, CancellationToken ct)
    {
        var authorization = scopeAuthorizer.ResolveAuthorizedScope(HttpContext);
        if (authorization is ErpInternalServiceScopeAuthorization.MissingRequiredHeader)
        {
            await ErpMachineOverheadEndpointResults.WriteMissingScopeHeadersAsync(HttpContext, ct);
            return;
        }
        if (authorization is ErpInternalServiceScopeAuthorization.Forbidden)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }
        var scope = ((ErpInternalServiceScopeAuthorization.Authorized)authorization).Scope;
        var response = await sender.Send(new ListWorkCenterMachineOverheadReconciliationsQuery(
            scope.OrganizationId,
            scope.EnvironmentId,
            req.AccountingPeriodCode,
            req.WorkCenterId,
            req.PageNumber,
            req.PageSize), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed record ErpInternalServiceScope(string OrganizationId, string EnvironmentId, string Actor);
public abstract record ErpInternalServiceScopeAuthorization
{
    private ErpInternalServiceScopeAuthorization()
    {
    }

    public sealed record Authorized(ErpInternalServiceScope Scope) : ErpInternalServiceScopeAuthorization;
    public sealed record MissingRequiredHeader : ErpInternalServiceScopeAuthorization;
    public sealed record Forbidden : ErpInternalServiceScopeAuthorization;
}

public interface IErpMachineOverheadInternalScopeAuthorizer
{
    ErpInternalServiceScopeAuthorization ResolveAuthorizedScope(HttpContext context);
}

public sealed class AuthenticatedErpMachineOverheadInternalScopeAuthorizer
    : IErpMachineOverheadInternalScopeAuthorizer
{
    public ErpInternalServiceScopeAuthorization ResolveAuthorizedScope(HttpContext context)
    {
        var requestedOrganizationId = OptionalHeader(context, "X-Organization-Id");
        var requestedEnvironmentId = OptionalHeader(context, "X-Environment-Id");
        if (requestedOrganizationId is null || requestedEnvironmentId is null)
            return new ErpInternalServiceScopeAuthorization.MissingRequiredHeader();

        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var authorizedOrganizationId = context.User.FindFirstValue(ScopedCallerClaimTypes.OrganizationId);
        var authorizedEnvironmentId = context.User.FindFirstValue(ScopedCallerClaimTypes.EnvironmentId);
        if (string.IsNullOrWhiteSpace(subject)
            || !string.Equals(requestedOrganizationId, authorizedOrganizationId, StringComparison.Ordinal)
            || !string.Equals(requestedEnvironmentId, authorizedEnvironmentId, StringComparison.Ordinal))
        {
            return new ErpInternalServiceScopeAuthorization.Forbidden();
        }

        return new ErpInternalServiceScopeAuthorization.Authorized(
            new(requestedOrganizationId, requestedEnvironmentId, $"internal-service:{subject}"));
    }

    private static string? OptionalHeader(HttpContext context, string name)
    {
        var value = context.Request.Headers[name].ToString().Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

internal static class ErpMachineOverheadEndpointResults
{
    private const string MissingScopeHeadersMessage =
        "X-Organization-Id and X-Environment-Id headers are required.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteMissingScopeHeadersAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new ResponseData(false, MissingScopeHeadersMessage, StatusCodes.Status400BadRequest, []),
            JsonOptions,
            cancellationToken);
    }
}
