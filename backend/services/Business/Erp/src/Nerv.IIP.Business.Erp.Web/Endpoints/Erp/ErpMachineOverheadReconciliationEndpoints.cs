using System.Security.Claims;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using NetCorePal.Extensions.Primitives;

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
        var scope = scopeAuthorizer.ResolveAuthorizedScope(HttpContext);
        if (scope is null)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }
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
        var scope = scopeAuthorizer.ResolveAuthorizedScope(HttpContext);
        if (scope is null)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }
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

public interface IErpMachineOverheadInternalScopeAuthorizer
{
    ErpInternalServiceScope? ResolveAuthorizedScope(HttpContext context);
}

public sealed class AuthenticatedErpMachineOverheadInternalScopeAuthorizer
    : IErpMachineOverheadInternalScopeAuthorizer
{
    public ErpInternalServiceScope? ResolveAuthorizedScope(HttpContext context)
    {
        var requestedOrganizationId = RequiredHeader(context, "X-Organization-Id");
        var requestedEnvironmentId = RequiredHeader(context, "X-Environment-Id");
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var authorizedOrganizationId = context.User.FindFirstValue(
            MachineOverheadInternalCallerAuthentication.OrganizationClaim);
        var authorizedEnvironmentId = context.User.FindFirstValue(
            MachineOverheadInternalCallerAuthentication.EnvironmentClaim);
        if (string.IsNullOrWhiteSpace(subject)
            || !string.Equals(requestedOrganizationId, authorizedOrganizationId, StringComparison.Ordinal)
            || !string.Equals(requestedEnvironmentId, authorizedEnvironmentId, StringComparison.Ordinal))
        {
            return null;
        }

        return new(requestedOrganizationId, requestedEnvironmentId, $"internal-service:{subject}");
    }

    private static string RequiredHeader(HttpContext context, string name)
    {
        var value = context.Request.Headers[name].ToString().Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new KnownException($"{name} header is required.")
            : value;
    }
}
