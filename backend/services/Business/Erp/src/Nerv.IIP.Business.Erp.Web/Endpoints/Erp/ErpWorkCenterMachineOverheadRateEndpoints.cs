using System.Text.Json.Serialization;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

namespace Nerv.IIP.Business.Erp.Web.Endpoints.Erp;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigureWorkCenterMachineOverheadRateRequest(
    string WorkCenterId,
    string AccountingPeriodCode,
    MachineOverheadApplicability Applicability,
    decimal FixedOverheadBudget,
    decimal VariableOverheadBudget,
    decimal NormalCapacityMachineHours,
    string CurrencyCode,
    string Reason);

public sealed record ConfigureWorkCenterMachineOverheadRateResponse(
    WorkCenterMachineOverheadRateId WorkCenterMachineOverheadRateId);

public sealed record ListWorkCenterMachineOverheadRatesRequest(
    string WorkCenterId,
    string AccountingPeriodCode,
    int PageNumber = 1,
    int PageSize = 50);

public sealed record GetCurrentWorkCenterMachineOverheadRateRequest(
    string WorkCenterId,
    string AccountingPeriodCode);

public sealed class ConfigureWorkCenterMachineOverheadRateEndpoint(
    ISender sender,
    IErpMachineOverheadInternalScopeAuthorizer scopeAuthorizer,
    TimeProvider timeProvider)
    : ErpEndpoint<ConfigureWorkCenterMachineOverheadRateRequest, ResponseData<ConfigureWorkCenterMachineOverheadRateResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ConfigureWorkCenterMachineOverheadRateEndpoint>());

    public override async Task HandleAsync(ConfigureWorkCenterMachineOverheadRateRequest req, CancellationToken ct)
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
        var id = await sender.Send(new ConfigureWorkCenterMachineOverheadRateCommand(
            scope.OrganizationId, scope.EnvironmentId, req.WorkCenterId, req.AccountingPeriodCode,
            req.Applicability, req.FixedOverheadBudget, req.VariableOverheadBudget,
            req.NormalCapacityMachineHours, req.CurrencyCode, scope.Actor, req.Reason,
            timeProvider.GetUtcNow()), ct);
        await Send.OkAsync(new ConfigureWorkCenterMachineOverheadRateResponse(id).AsResponseData(), cancellation: ct);
    }
}

public sealed class ListWorkCenterMachineOverheadRatesEndpoint(
    ISender sender,
    IErpMachineOverheadInternalScopeAuthorizer scopeAuthorizer)
    : ErpEndpoint<ListWorkCenterMachineOverheadRatesRequest, ResponseData<ListWorkCenterMachineOverheadRatesResponse>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<ListWorkCenterMachineOverheadRatesEndpoint>());

    public override async Task HandleAsync(ListWorkCenterMachineOverheadRatesRequest req, CancellationToken ct)
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
        var response = await sender.Send(new ListWorkCenterMachineOverheadRatesQuery(
            scope.OrganizationId, scope.EnvironmentId, req.WorkCenterId, req.AccountingPeriodCode,
            req.PageNumber, req.PageSize), ct);
        await Send.OkAsync(response.AsResponseData(), cancellation: ct);
    }
}

public sealed class GetCurrentWorkCenterMachineOverheadRateEndpoint(
    ISender sender,
    IErpMachineOverheadInternalScopeAuthorizer scopeAuthorizer)
    : ErpEndpoint<GetCurrentWorkCenterMachineOverheadRateRequest, ResponseData<WorkCenterMachineOverheadRateListItem>>
{
    public override void Configure() => ConfigureErpContract(ErpFinanceEndpointContracts.Get<GetCurrentWorkCenterMachineOverheadRateEndpoint>());

    public override async Task HandleAsync(GetCurrentWorkCenterMachineOverheadRateRequest req, CancellationToken ct)
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
        var response = await sender.Send(new ListWorkCenterMachineOverheadRatesQuery(
            scope.OrganizationId, scope.EnvironmentId, req.WorkCenterId, req.AccountingPeriodCode,
            PageSize: 1), ct);
        var current = response.Items.FirstOrDefault()
            ?? throw new KnownException("指定工作中心与会计期间缺少适用或明确不适用的机器制造费用率。");
        await Send.OkAsync(current.AsResponseData(), cancellation: ct);
    }
}
