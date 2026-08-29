using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
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
            ConfigurationErpMachineOverheadInternalScopeAuthorizer.SystemActor,
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

public sealed record ErpInternalServiceScope(string OrganizationId, string EnvironmentId);

public interface IErpMachineOverheadInternalScopeAuthorizer
{
    ErpInternalServiceScope ResolveAuthorizedScope(HttpContext context);
}

public sealed class ConfigurationErpMachineOverheadInternalScopeAuthorizer(IConfiguration configuration)
    : IErpMachineOverheadInternalScopeAuthorizer
{
    public const string SystemActor = "system:business-erp-finance-reconciliation";

    private readonly HashSet<string> authorizedScopes = configuration
        .GetSection("Erp:MachineOverheadReconciliation:AuthorizedScopes")
        .GetChildren()
        .Select(scope => ScopeKey(
            scope["OrganizationId"] ?? string.Empty,
            scope["EnvironmentId"] ?? string.Empty))
        .Where(key => key.Length > 1)
        .ToHashSet(StringComparer.Ordinal);

    public ErpInternalServiceScope ResolveAuthorizedScope(HttpContext context)
    {
        var scope = new ErpInternalServiceScope(
            RequiredHeader(context, "X-Organization-Id"),
            RequiredHeader(context, "X-Environment-Id"));
        if (!authorizedScopes.Contains(ScopeKey(scope.OrganizationId, scope.EnvironmentId)))
            throw new KnownException("Internal service is not authorized for the requested ERP machine-overhead reconciliation scope.");
        return scope;
    }

    private static string RequiredHeader(HttpContext context, string name)
    {
        var value = context.Request.Headers[name].ToString().Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new KnownException($"{name} header is required.")
            : value;
    }

    private static string ScopeKey(string organizationId, string environmentId)
    {
        var organization = organizationId.Trim();
        var environment = environmentId.Trim();
        return string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(environment)
            ? string.Empty
            : $"{organization}\n{environment}";
    }
}
